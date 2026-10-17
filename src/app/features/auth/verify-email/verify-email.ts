import { Component, DestroyRef, ElementRef, OnInit, computed, inject, signal, viewChildren } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { ApiError } from '../../../core/models/response-cls.model';
import { AuthShell } from '../auth-shell/auth-shell';

const CODE_LENGTH = 6;

@Component({
  selector: 'app-verify-email',
  imports: [RouterLink, AuthShell],
  templateUrl: './verify-email.html',
  styleUrl: './verify-email.scss'
})
export class VerifyEmail implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly boxes = viewChildren<ElementRef<HTMLInputElement>>('box');

  readonly email = signal('');
  readonly digits = signal<string[]>(Array(CODE_LENGTH).fill(''));
  readonly verifying = signal(false);
  readonly resending = signal(false);
  readonly errorMessage = signal('');
  readonly infoMessage = signal('');
  readonly verified = signal(false);
  readonly resendIn = signal(0);

  readonly code = computed(() => this.digits().join(''));
  readonly complete = computed(() => /^\d{6}$/.test(this.code()));

  private timer: ReturnType<typeof setInterval> | null = null;

  ngOnInit(): void {
    const email = this.auth.getPendingVerification();
    if (!email) {
      void this.router.navigate(['/register']);
      return;
    }

    this.email.set(email);
    // Navigation state from register/login (only set when navigating in-app).
    const state = (typeof history !== 'undefined' ? history.state : null) as
      | { message?: string; resendAfterSeconds?: number }
      | null;
    this.infoMessage.set(state?.message ?? `Enter the 6-digit code we sent to ${email}.`);
    this.startCooldown(state?.resendAfterSeconds ?? 60);
    if (state?.resendAfterSeconds === 0) {
      this.errorMessage.set(this.infoMessage());
      this.infoMessage.set(`Request a new code for ${email}.`);
    }
    this.destroyRef.onDestroy(() => this.stopTimer());
  }

  onInput(index: number, event: Event): void {
    const input = event.target as HTMLInputElement;
    const value = input.value.replace(/\D/g, '');

    if (value.length > 1) {
      // Typed fast or autofilled: spread across the boxes.
      this.fillFrom(index, value);
      return;
    }

    this.setDigit(index, value);
    input.value = value;
    if (value && index < CODE_LENGTH - 1) {
      this.focus(index + 1);
    }
    this.autoSubmit();
  }

  onKeydown(index: number, event: KeyboardEvent): void {
    if (event.key === 'Backspace' && !this.digits()[index] && index > 0) {
      this.setDigit(index - 1, '');
      this.focus(index - 1);
      event.preventDefault();
    } else if (event.key === 'ArrowLeft' && index > 0) {
      this.focus(index - 1);
    } else if (event.key === 'ArrowRight' && index < CODE_LENGTH - 1) {
      this.focus(index + 1);
    }
  }

  onPaste(event: ClipboardEvent): void {
    const pasted = event.clipboardData?.getData('text').replace(/\D/g, '') ?? '';
    if (pasted) {
      event.preventDefault();
      this.fillFrom(0, pasted);
    }
  }

  async verify(): Promise<void> {
    if (!this.complete() || this.verifying()) {
      return;
    }

    this.verifying.set(true);
    this.errorMessage.set('');
    try {
      const response = await this.auth.verifyOtp(this.email(), this.code());
      this.verified.set(true);
      this.infoMessage.set(response.statusMessage);
      this.auth.clearPendingVerification();
      this.stopTimer();
      setTimeout(() => void this.router.navigate(['/login'], { queryParams: { verified: 1, email: this.email() } }), 1200);
    } catch (error) {
      this.errorMessage.set(ApiError.from(error).message);
      this.digits.set(Array(CODE_LENGTH).fill(''));
      this.boxes().forEach(b => (b.nativeElement.value = ''));
      this.focus(0);
    } finally {
      this.verifying.set(false);
    }
  }

  async resend(): Promise<void> {
    if (this.resendIn() > 0 || this.resending()) {
      return;
    }

    this.resending.set(true);
    this.errorMessage.set('');
    try {
      const response = await this.auth.resendOtp(this.email());
      this.infoMessage.set(response.statusMessage);
      this.startCooldown(response.statusObject?.resendAfterSeconds ?? 60);
    } catch (error) {
      this.errorMessage.set(ApiError.from(error).message);
    } finally {
      this.resending.set(false);
    }
  }

  private fillFrom(start: number, value: string): void {
    const next = [...this.digits()];
    for (let i = 0; i < value.length && start + i < CODE_LENGTH; i++) {
      next[start + i] = value[i];
    }
    this.digits.set(next);
    this.boxes().forEach((b, i) => (b.nativeElement.value = next[i]));
    this.focus(Math.min(start + value.length, CODE_LENGTH - 1));
    this.autoSubmit();
  }

  private setDigit(index: number, value: string): void {
    this.digits.update(d => d.map((v, i) => (i === index ? value : v)));
  }

  private focus(index: number): void {
    this.boxes()[index]?.nativeElement.focus();
  }

  private autoSubmit(): void {
    if (this.complete()) {
      void this.verify();
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
