import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { ApiError } from '../../../core/models/response-cls.model';
import {
  applyServerErrors,
  matchFields,
  passwordScore,
  strongPassword
} from '../../../shared/validators/form.validators';
import { AuthShell } from '../auth-shell/auth-shell';

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink, AuthShell],
  templateUrl: './register.html',
  styleUrl: './register.scss'
})
export class Register {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly submitting = signal(false);
  readonly errorMessage = signal('');
  readonly showPassword = signal(false);
  readonly showTerms = signal(false);

  readonly form = this.fb.nonNullable.group(
    {
      fullName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100), Validators.pattern(/^[\p{L} .'-]+$/u)]],
      email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
      phoneNumber: ['', [Validators.required, Validators.pattern(/^[6-9]\d{9}$/)]],
      password: ['', [Validators.required, Validators.maxLength(128), strongPassword]],
      confirmPassword: ['', [Validators.required]],
      acceptTerms: [false, [Validators.requiredTrue]]
    },
    { validators: matchFields('password', 'confirmPassword') }
  );

  private readonly passwordValue = toSignal(this.form.controls.password.valueChanges, { initialValue: '' });

  readonly strength = computed(() => passwordScore(this.passwordValue()));
  readonly strengthLabel = computed(() => ['Too weak', 'Weak', 'Fair', 'Good', 'Strong'][this.strength()]);

  readonly passwordRules = computed(() => {
    const value = this.passwordValue();
    return [
      { label: '8+ characters', ok: value.length >= 8 },
      { label: 'Uppercase & lowercase', ok: /[A-Z]/.test(value) && /[a-z]/.test(value) },
      { label: 'A number', ok: /\d/.test(value) },
      { label: 'A symbol', ok: /[^A-Za-z\d]/.test(value) }
    ];
  });

  showError(name: keyof typeof this.form.controls): boolean {
    const control = this.form.controls[name];
    return control.invalid && (control.touched || control.dirty);
  }

  errorFor(name: keyof typeof this.form.controls): string {
    const errors = this.form.controls[name].errors;
    if (!errors) return '';
    if (errors['server']) return errors['server'];

    switch (name) {
      case 'fullName':
        if (errors['required']) return 'Enter your full name.';
        if (errors['pattern']) return "Use letters, spaces, . ' and - only.";
        return 'Full name must be 2-100 characters.';
      case 'email':
        return errors['required'] ? 'Enter your email address.' : 'Enter a valid email address.';
      case 'phoneNumber':
        return errors['required'] ? 'Enter your mobile number.' : 'Enter a valid 10-digit Indian mobile number.';
      case 'password':
        return errors['required'] ? 'Create a password.' : 'Password doesn’t meet the requirements below.';
      case 'confirmPassword':
        return errors['required'] ? 'Confirm your password.' : 'Passwords do not match.';
      case 'acceptTerms':
        return 'Please accept the Terms & Conditions to continue.';
    }
  }

  async submit(): Promise<void> {
    this.errorMessage.set('');
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    try {
      const value = this.form.getRawValue();
      const response = await this.auth.register({ ...value, email: value.email.trim(), fullName: value.fullName.trim() });
      const email = response.statusObject?.email ?? value.email.trim().toLowerCase();

      this.auth.setPendingVerification(email);
      await this.router.navigate(['/verify-email'], {
        state: { message: response.statusMessage, resendAfterSeconds: response.statusObject?.resendAfterSeconds }
      });
    } catch (error) {
      const apiError = ApiError.from(error);
      if (!applyServerErrors(this.form, apiError.fieldErrors)) {
        this.errorMessage.set(apiError.message);
      }
    } finally {
      this.submitting.set(false);
    }
  }
}
