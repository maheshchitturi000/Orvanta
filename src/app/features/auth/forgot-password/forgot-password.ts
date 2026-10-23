import { Component, DestroyRef, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { ApiError } from '../../../core/models/response-cls.model';
import { OtpInput } from '../../../shared/ui/otp-input/otp-input';
import { PasswordStrength } from '../../../shared/ui/password-strength/password-strength';
import { applyServerErrors, matchFields, strongPassword } from '../../../shared/validators/form.validators';
import { AuthShell } from '../auth-shell/auth-shell';

type Step = 'email' | 'code' | 'password';

const HEADINGS: Record<Step, string> = {
  email: 'Forgot your password?',
  code: 'Check your email',
  password: 'Choose a new password'
};

/**
 * Forgot password: email → 6-digit code → new password → back to sign in.
 * The code is checked in step 2 and used up in step 3, together with the new password.
 */
@Component({
  selector: 'app-forgot-password',
  imports: [ReactiveFormsModule, RouterLink, AuthShell, OtpInput, PasswordStrength],
  templateUrl: './forgot-password.html',
  styleUrl: './forgot-password.scss'
})
export class ForgotPassword implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly otp = viewChild(OtpInput);

  protected readonly headings = HEADINGS;
  protected readonly steps: { key: Step; label: string }[] = [
    { key: 'email', label: 'Email' },
    { key: 'code', label: 'Verify' },
    { key: 'password', label: 'New password' }
  ];

  protected readonly step = signal<Step>('email');
  protected readonly busy = signal(false);
  protected readonly errorMessage = signal('');
  protected readonly code = signal('');
  protected readonly codeComplete = computed(() => /^\d{6}$/.test(this.code()));
  protected readonly resendIn = signal(0);
  protected readonly showPassword = signal(false);

  protected readonly emailForm = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]]
  });

  protected readonly passwordForm = this.fb.nonNullable.group(
    {
      newPassword: ['', [Validators.required, Validators.maxLength(128), strongPassword]],
      confirmPassword: ['', [Validators.required]]
    },
    { validators: matchFields('newPassword', 'confirmPassword') }
  );

  protected readonly newPasswordValue = toSignal(this.passwordForm.controls.newPassword.valueChanges, { initialValue: '' });

  private timer: ReturnType<typeof setInterval> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stopTimer());
  }

  ngOnInit(): void {
    const email = this.route.snapshot.queryParamMap.get('email');
    if (email) {
      this.emailForm.controls.email.setValue(email);
    }
  }

  protected get email(): string {
    return this.emailForm.controls.email.value.trim().toLowerCase();
  }

  protected stepIndex(step: Step): number {
    return this.steps.findIndex(s => s.key === step);
  }

  // ---------- Step 1: email ----------

  protected async sendCode(): Promise<void> {
    if (this.emailForm.invalid) {
      this.emailForm.markAllAsTouched();
      return;
    }
    await this.requestCode();
  }

  protected async resend(): Promise<void> {
    if (this.resendIn() === 0 && !this.busy()) {
      await this.requestCode();
      this.otp()?.clear();
    }
  }

  private async requestCode(): Promise<void> {
    this.busy.set(true);
    this.errorMessage.set('');
    try {
      const response = await this.auth.forgotPassword(this.email);
      this.startCooldown(response.statusObject?.resendAfterSeconds ?? 60);
      this.code.set('');
      this.step.set('code');
      this.otp()?.focus();
    } catch (error) {
      const apiError = ApiError.from(error);
      if (!applyServerErrors(this.emailForm, apiError.fieldErrors)) {
        this.errorMessage.set(apiError.message);
      }
    } finally {
      this.busy.set(false);
    }
  }

  // ---------- Step 2: code ----------

  protected async verifyCode(): Promise<void> {
    if (!this.codeComplete() || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.errorMessage.set('');
    try {
      await this.auth.verifyResetCode(this.email, this.code());
      this.step.set('password');
      setTimeout(() => document.getElementById('newPassword')?.focus());
    } catch (error) {
      this.errorMessage.set(ApiError.from(error).message);
      this.otp()?.clear();
    } finally {
      this.busy.set(false);
    }
  }

  protected changeEmail(): void {
    this.stopTimer();
    this.resendIn.set(0);
    this.errorMessage.set('');
    this.step.set('email');
  }

  // ---------- Step 3: new password ----------

  protected showError(name: 'newPassword' | 'confirmPassword'): boolean {
    const control = this.passwordForm.controls[name];
    return control.invalid && (control.touched || control.dirty);
  }

  protected errorFor(name: 'newPassword' | 'confirmPassword'): string {
    const errors = this.passwordForm.controls[name].errors ?? {};
    if (errors['server']) return errors['server'];
    if (name === 'newPassword') {
      return errors['required'] ? 'Create a new password.' : 'Password doesn’t meet the requirements below.';
    }
    return errors['required'] ? 'Confirm your new password.' : 'Passwords do not match.';
  }

  protected async resetPassword(): Promise<void> {
    if (this.passwordForm.invalid) {
      this.passwordForm.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.errorMessage.set('');
    const { newPassword, confirmPassword } = this.passwordForm.getRawValue();
    try {
      await this.auth.resetPassword({ email: this.email, code: this.code(), newPassword, confirmPassword });
      this.stopTimer();
      await this.router.navigate(['/login'], { queryParams: { reset: 1, email: this.email } });
    } catch (error) {
      const apiError = ApiError.from(error);
      if (applyServerErrors(this.passwordForm, apiError.fieldErrors)) {
        return;
      }
      // The code expired or ran out of attempts meanwhile: get a new one.
      this.errorMessage.set(apiError.message);
      this.code.set('');
      this.step.set('code');
      this.otp()?.focus();
    } finally {
      this.busy.set(false);
    }
  }

  private startCooldown(seconds: number): void {
    this.stopTimer();
    this.resendIn.set(seconds);
    this.timer = setInterval(() => {
      this.resendIn.update(s => Math.max(s - 1, 0));
      if (this.resendIn() === 0) {
        this.stopTimer();
      }
    }, 1000);
  }

  private stopTimer(): void {
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }
}
