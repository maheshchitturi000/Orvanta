import { isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ApiService } from '../http/api.service';
import { ApiError, ResponseCls } from '../models/response-cls.model';
import {
  AuthSession,
  OtpSentResult,
  RegisterRequest,
  RegisterResult,
  UserProfile
} from './auth.models';

const PENDING_EMAIL_KEY = 'orv_pending_email';

/**
 * Holds the signed-in session.
 * - The access token (encrypted JWT) is kept in memory only, never in localStorage.
 * - The refresh token lives in an HttpOnly cookie that JavaScript can't read;
 *   it's used to restore the session on reload and to renew expired tokens.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  private readonly _user = signal<UserProfile | null>(null);
  private accessTokenValue: string | null = null;
  private refreshInFlight: Promise<boolean> | null = null;
  private refreshTimer: ReturnType<typeof setTimeout> | null = null;

  readonly user = this._user.asReadonly();
  readonly isAuthenticated = computed(() => this._user() !== null);
  readonly firstName = computed(() => this._user()?.fullName.split(' ')[0] ?? '');

  get accessToken(): string | null {
    return this.accessTokenValue;
  }

  /** Called once at startup: silently restores the session from the refresh cookie. */
  async restoreSession(): Promise<void> {
    if (this.isBrowser) {
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

  async logout(): Promise<void> {
    try {
      await this.api.post('/auth/logout');
    } catch (error) {
      // Signing out locally is what matters; the server session expires on its own.
      console.warn('Logout request failed', error);
    }
    this.clearSession();
    await this.router.navigate(['/login']);
  }

  /** Called by the interceptor when a session can't be renewed. */
  async handleSessionExpired(): Promise<void> {
    this.clearSession();
    await this.router.navigate(['/login'], {
      queryParams: { returnUrl: this.router.url, reason: 'expired' }
    });
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

  private setSession(session: AuthSession): void {
    this.accessTokenValue = session.accessToken;
    this._user.set(session.user);
    this.scheduleRefresh(session.expiresInSeconds);
  }

  private clearSession(): void {
    this.accessTokenValue = null;
    this._user.set(null);
    if (this.refreshTimer) {
      clearTimeout(this.refreshTimer);
      this.refreshTimer = null;
    }
  }

  /** Renews the access token a minute before it expires so requests never hit a 401 mid-flow. */
  private scheduleRefresh(expiresInSeconds: number): void {
    if (!this.isBrowser) {
      return;
    }
    if (this.refreshTimer) {
      clearTimeout(this.refreshTimer);
    }
    const delayMs = Math.max(expiresInSeconds - 60, 15) * 1000;
    this.refreshTimer = setTimeout(() => void this.refresh(), delayMs);
  }
}
