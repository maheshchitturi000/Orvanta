import { AbstractControl, FormGroup, ValidationErrors, ValidatorFn } from '@angular/forms';
import { FieldErrors } from '../../core/models/response-cls.model';

/** Same policy as the AuthService: 8+ chars with upper, lower, digit and symbol. */
export const strongPassword: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const value = (control.value as string) ?? '';
  if (!value) {
    return null;
  }

  const missing: string[] = [];
  if (value.length < 8) missing.push('8+ characters');
  if (!/[A-Z]/.test(value)) missing.push('an uppercase letter');
  if (!/[a-z]/.test(value)) missing.push('a lowercase letter');
  if (!/\d/.test(value)) missing.push('a number');
  if (!/[^A-Za-z\d]/.test(value)) missing.push('a symbol');

  return missing.length ? { strongPassword: { missing } } : null;
};

/** Group validator: sets `mismatch` on the confirm control when the two fields differ. */
export function matchFields(field: string, confirmField: string): ValidatorFn {
  return (group: AbstractControl): ValidationErrors | null => {
    const source = group.get(field);
    const confirm = group.get(confirmField);
    if (!source || !confirm) {
      return null;
    }

    const mismatch = !!confirm.value && source.value !== confirm.value;
    const { mismatch: _ignored, ...otherErrors } = confirm.errors ?? {};
    confirm.setErrors(mismatch ? { ...otherErrors, mismatch: true } : Object.keys(otherErrors).length ? otherErrors : null);
    return null;
  };
}

/** 0-4 strength score for the password meter. */
export function passwordScore(value: string): number {
  if (!value) {
    return 0;
  }
  let score = 0;
  if (value.length >= 8) score++;
  if (value.length >= 12) score++;
  if (/[A-Z]/.test(value) && /[a-z]/.test(value)) score++;
  if (/\d/.test(value) && /[^A-Za-z\d]/.test(value)) score++;
  return score;
}

/** Puts server-side field errors (ResponseCls StatusObject) onto matching form controls. */
export function applyServerErrors(form: FormGroup, errors: FieldErrors | null): boolean {
  if (!errors) {
    return false;
  }
  let applied = false;
  for (const [key, messages] of Object.entries(errors)) {
    const control = form.get(key);
    if (control && messages.length) {
      control.setErrors({ ...(control.errors ?? {}), server: messages[0] });
      control.markAsTouched();
      applied = true;
    }
  }
  return applied;
}
