import { isPlatformBrowser } from '@angular/common';
import { DestroyRef, Injectable, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ApiService } from '../http/api.service';
import { ApiError, ResponseCls } from '../models/response-cls.model';
import {
  AuthSession,
  OtpSentResult,
  RegisterRequest,
  RegisterResult,
  ResetPasswordRequest,
  UserProfile
} from './auth.models';

const PENDING_EMAIL_KEY = 'orv_pending_email';

/**
 * "This browser has a session" marker (no token, just a flag). The refresh cookie is HttpOnly, so without it a
 * signed-out visitor would always call /auth/refresh on load and get a 401 (a red error in the console).
 */
const SESSION_HINT_KEY = 'orv_has_session';

/** How long before the access token expires the "Stay signed in?" prompt appears. */
export const SESSION_WARNING_LEAD_SECONDS = 120;

/** Keeps every open tab in step: continuing or signing out in one tab applies to all. */
const SESSION_CHANNEL = 'orv_session';
type SessionMessage = { type: 'refreshed' } | { type: 'signed-out'; reason: 'logout' | 'expired' };

/**
 * Holds the signed-in session.
 * - The access token (encrypted JWT, 15 min) is kept in memory only, never in localStorage.
 * - The refresh token (30 min, rotated on every use) lives in an HttpOnly cookie JavaScript can't read;
 *   it restores the session on reload and renews the access token.
 * - The access token is NOT renewed silently: shortly before it expires the user is asked whether to
 *   stay signed in. Continuing renews it; no answer ends the session (idle timeout).
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  private readonly _user = signal<UserProfile | null>(null);
  private readonly _expiresAt = signal<number | null>(null);
  private readonly _warning = signal(false);
  private readonly _now = signal(Date.now());

  private accessTokenValue: string | null = null;
  private refreshInFlight: Promise<boolean> | null = null;
  private warningTimer: ReturnType<typeof setTimeout> | null = null;
  private expiryTimer: ReturnType<typeof setTimeout> | null = null;
  private clockTimer: ReturnType<typeof setInterval> | null = null;
  private readonly channel = this.isBrowser && 'BroadcastChannel' in window ? new BroadcastChannel(SESSION_CHANNEL) : null;

  readonly user = this._user.asReadonly();
  readonly isAuthenticated = computed(() => this._user() !== null);
  readonly firstName = computed(() => this._user()?.fullName.split(' ')[0] ?? '');
  /** Up to two initials for avatars ("Mahesh Chitturi" → "MC"). */
  readonly initials = computed(() =>
    (this._user()?.fullName ?? '?')
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map(part => part[0]!.toUpperCase())
      .join('')
  );

  /** True while the "Stay signed in?" prompt should be shown. */
  readonly sessionWarning = this._warning.asReadonly();
  /** When the current access token expires (epoch ms), or null when signed out. */
  readonly expiresAt = this._expiresAt.asReadonly();
  /** Whole seconds until the access token expires; ticks every second while signed in. */
  readonly secondsLeft = computed(() => {
    const expiresAt = this._expiresAt();
    return expiresAt === null ? 0 : Math.max(0, Math.ceil((expiresAt - this._now()) / 1000));
  });

  constructor() {
    if (this.channel) {
      this.channel.onmessage = (event: MessageEvent<SessionMessage>) => this.onTabMessage(event.data);
      inject(DestroyRef).onDestroy(() => this.channel?.close());
    }
  }

  get accessToken(): string | null {
    return this.accessTokenValue;
  }

  /** Called once at startup: silently restores the session from the refresh cookie. */
  async restoreSession(): Promise<void> {
    if (this.isBrowser && this.hasSessionHint()) {
      await this.refresh();
    }
  }

  register(request: RegisterRequest): Promise<ResponseCls<RegisterResult>> {
    return this.api.post<RegisterResult>('/auth/register', request);
  }

  verifyOtp(email: string, code: string): Promise<ResponseCls<{ verified: boolean }>> {
    return this.api.post<{ verified: boolean }>('/auth/verify-otp', { email, code });
  }

  resendOtp(email: string): Promise<ResponseCls<OtpSentResult>> {
    return this.api.post<OtpSentResult>('/auth/resend-otp', { email });
  }

  /** Forgot password, step 1: emails a reset code (same answer whether or not the account exists). */
  forgotPassword(email: string): Promise<ResponseCls<OtpSentResult>> {
    return this.api.post<OtpSentResult>('/auth/forgot-password', { email });
  }

  /** Step 2: checks the code without using it up. */
  verifyResetCode(email: string, code: string): Promise<ResponseCls<{ verified: boolean }>> {
    return this.api.post<{ verified: boolean }>('/auth/verify-reset-code', { email, code });
  }

  /** Step 3: sets the new password. The account is signed out on every device. */
  resetPassword(request: ResetPasswordRequest): Promise<ResponseCls<{ reset: boolean }>> {
    return this.api.post<{ reset: boolean }>('/auth/reset-password', request);
  }

  async login(email: string, password: string): Promise<ResponseCls<AuthSession>> {
    const response = await this.api.post<AuthSession>('/auth/login', { email, password });
    if (response.statusObject) {
      this.setSession(response.statusObject);
    }
    return response;
  }

  /** Exchanges the refresh cookie for a new access token. Concurrent callers share one request. */
  refresh(): Promise<boolean> {
    if (!this.isBrowser) {
      return Promise.resolve(false);
    }

    this.refreshInFlight ??= this.api
      .post<AuthSession>('/auth/refresh')
      .then(response => {
        if (!response.statusObject) {
          this.clearSession();
          return false;
        }
        this.setSession(response.statusObject);
        return true;
      })
      .catch(() => {
        this.clearSession();
        return false;
      })
      .finally(() => (this.refreshInFlight = null));

    return this.refreshInFlight;
  }

  /** "Stay signed in": renews the access token with the refresh token and tells the other tabs. */
  async continueSession(): Promise<boolean> {
    const renewed = await this.refresh();
    if (renewed) {
      this.broadcast({ type: 'refreshed' });
    } else {
      await this.navigateToLogin('expired');
    }
    return renewed;
  }

  async logout(): Promise<void> {
    await this.endSession('logout');
  }

  /** Called by the interceptor when a session can't be renewed. */
  async handleSessionExpired(): Promise<void> {
    this.clearSession();
    await this.navigateToLogin('expired');
  }

  // Remembers which email is waiting for OTP so a page reload doesn't lose it.
  setPendingVerification(email: string): void {
    if (this.isBrowser) {
      sessionStorage.setItem(PENDING_EMAIL_KEY, email);
    }
  }

  getPendingVerification(): string | null {
    return this.isBrowser ? sessionStorage.getItem(PENDING_EMAIL_KEY) : null;
  }

  clearPendingVerification(): void {
    if (this.isBrowser) {
      sessionStorage.removeItem(PENDING_EMAIL_KEY);
    }
  }

  isVerificationRequired(error: unknown): string | null {
    if (error instanceof ApiError && error.statusCode === 403) {
      const payload = error.statusObject as { requiresVerification?: boolean; email?: string } | null;
      return payload?.requiresVerification ? payload.email ?? null : null;
    }
    return null;
  }

  /** Revokes the server session (so the refresh cookie is dead everywhere), then signs out locally. */
  private async endSession(reason: 'logout' | 'expired'): Promise<void> {
    this.clearSession();
    try {
      await this.api.post('/auth/logout');
    } catch (error) {
      // Signing out locally is what matters; the refresh token also expires on its own.
      console.warn('Logout request failed', error);
    }
    this.broadcast({ type: 'signed-out', reason });
    await this.navigateToLogin(reason);
  }

  private onTabMessage(message: SessionMessage): void {
    if (message.type === 'refreshed' && this.isAuthenticated()) {
      // Another tab continued the session; pick up a fresh token so this tab's prompt closes too.
      void this.refresh();
    } else if (message.type === 'signed-out' && this.isAuthenticated()) {
      this.clearSession();
      void this.navigateToLogin(message.reason);
    }
  }

  private setSession(session: AuthSession): void {
    this.accessTokenValue = session.accessToken;
    this._user.set(session.user);
    this.scheduleExpiry(session.expiresInSeconds);
    this.setSessionHint(true);
  }

  private clearSession(): void {
    this.setSessionHint(false);
    this.accessTokenValue = null;
    this._user.set(null);
    this._expiresAt.set(null);
    this._warning.set(false);
    this.clearTimers();
  }

  /**
   * Uses the server's "expires in" (not its clock) so a skewed device clock can't break the timing.
   * Timers that fire late (sleeping laptop) still end the session correctly.
   */
  private scheduleExpiry(expiresInSeconds: number): void {
    if (!this.isBrowser) {
      return;
    }
    this.clearTimers();

    const expiresAt = Date.now() + expiresInSeconds * 1000;
    this._expiresAt.set(expiresAt);
    this._warning.set(false);
    this._now.set(Date.now());

    const warnInMs = Math.max((expiresInSeconds - SESSION_WARNING_LEAD_SECONDS) * 1000, 0);
    this.warningTimer = setTimeout(() => this._warning.set(true), warnInMs);
    this.expiryTimer = setTimeout(() => void this.endSession('expired'), expiresInSeconds * 1000);
    this.clockTimer = setInterval(() => this._now.set(Date.now()), 1000);
  }

  private clearTimers(): void {
    if (this.warningTimer) clearTimeout(this.warningTimer);
    if (this.expiryTimer) clearTimeout(this.expiryTimer);
    if (this.clockTimer) clearInterval(this.clockTimer);
    this.warningTimer = this.expiryTimer = this.clockTimer = null;
  }

  private broadcast(message: SessionMessage): void {
    this.channel?.postMessage(message);
  }

  /** After a timeout the user returns to the page they were on once signed in again. */
  private async navigateToLogin(reason: 'logout' | 'expired'): Promise<void> {
    const url = this.router.url;
    const keepReturnUrl = reason === 'expired' && !url.startsWith('/login');
    await this.router.navigate(['/login'], { queryParams: keepReturnUrl ? { returnUrl: url } : {} });
  }

  private hasSessionHint(): boolean {
    try {
      // '0' = signed out on this browser. No value yet (first visit, or signed in before this existed): try once.
      return localStorage.getItem(SESSION_HINT_KEY) !== '0';
    } catch {
      return true; // Storage blocked: fall back to always trying.
    }
  }

  private setSessionHint(present: boolean): void {
    if (!this.isBrowser) {
      return;
    }
    try {
      localStorage.setItem(SESSION_HINT_KEY, present ? '1' : '0');
    } catch {
      // Storage blocked: restoreSession simply always tries.
    }
  }
}
