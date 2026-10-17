import { FormControl, FormGroup } from '@angular/forms';
import { applyServerErrors, matchFields, passwordScore, strongPassword } from './form.validators';

describe('form validators', () => {
  it('strongPassword lists what is missing', () => {
    expect(strongPassword(new FormControl('Str0ng!Pass'))).toBeNull();
    expect(strongPassword(new FormControl(''))).toBeNull();

    const result = strongPassword(new FormControl('weak'));
    expect(result?.['strongPassword'].missing).toEqual([
      '8+ characters',
      'an uppercase letter',
      'a number',
      'a symbol'
    ]);
  });

  it('matchFields flags a mismatched confirmation and clears it when fixed', () => {
    const form = new FormGroup(
      { password: new FormControl('Abcdef1!'), confirm: new FormControl('nope') },
      { validators: matchFields('password', 'confirm') }
    );
    expect(form.controls.confirm.hasError('mismatch')).toBe(true);

    form.controls.confirm.setValue('Abcdef1!');
    expect(form.controls.confirm.hasError('mismatch')).toBe(false);
  });

  it('passwordScore grows with length and variety', () => {
    expect(passwordScore('')).toBe(0);
    expect(passwordScore('abcdefgh')).toBe(1);
    expect(passwordScore('Str0ng!Passw0rd')).toBe(4);
  });

  it('applyServerErrors maps ResponseCls field errors onto controls', () => {
    const form = new FormGroup({ email: new FormControl('a@b.c') });
    const applied = applyServerErrors(form, { email: ['Already registered.'], unknown: ['ignored'] });

    expect(applied).toBe(true);
    expect(form.controls.email.getError('server')).toBe('Already registered.');
  });
});
