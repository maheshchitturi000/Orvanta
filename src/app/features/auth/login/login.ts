import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { ApiError } from '../../../core/models/response-cls.model';
import { applyServerErrors } from '../../../shared/validators/form.validators';
import { AuthShell } from '../auth-shell/auth-shell';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink, AuthShell],
  templateUrl: './login.html',
  styleUrl: './login.scss'
})
export class Login implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly submitting = signal(false);
  readonly errorMessage = signal('');
  readonly infoMessage = signal('');
  readonly showPassword = signal(false);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]]
  });

  ngOnInit(): void {
    const params = this.route.snapshot.queryParamMap;
    if (params.get('reset')) {
      this.infoMessage.set('Your password has been updated. Sign in with your new password.');
    } else if (params.get('verified')) {
      this.infoMessage.set('Email verified! Sign in to get started.');
    }

    const email = params.get('email');
    if (email) {
      this.form.controls.email.setValue(email);
    }
  }

  /** Carries the email already typed over to the reset page. */
  forgotParams(): Record<string, string> {
    const email = this.form.controls.email.value.trim();
    return email ? { email } : {};
  }

  showError(name: 'email' | 'password'): boolean {
    const control = this.form.controls[name];
    return control.invalid && (control.touched || control.dirty);
  }

  errorFor(name: 'email' | 'password'): string {
    const errors = this.form.controls[name].errors ?? {};
    if (errors['server']) return errors['server'];
    if (name === 'email') return errors['required'] ? 'Enter your email address.' : 'Enter a valid email address.';
    return 'Enter your password.';
  }

  async submit(): Promise<void> {
    this.errorMessage.set('');
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    const { email, password } = this.form.getRawValue();

    try {
      await this.auth.login(email.trim(), password);
      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
      await this.router.navigateByUrl(returnUrl?.startsWith('/') && !returnUrl.startsWith('//') ? returnUrl : '/');
    } catch (error) {
      const pendingEmail = this.auth.isVerificationRequired(error);
      if (pendingEmail) {
        this.auth.setPendingVerification(pendingEmail);
        await this.router.navigate(['/verify-email'], { state: { message: ApiError.from(error).message } });
        return;
      }

      const apiError = ApiError.from(error);
      if (!applyServerErrors(this.form, apiError.fieldErrors)) {
        this.errorMessage.set(apiError.message);
      }
      this.form.controls.password.reset();
    } finally {
      this.submitting.set(false);
    }
  }
}
