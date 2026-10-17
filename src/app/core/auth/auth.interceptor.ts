import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { API_BASE_URL } from '../config/api.config';
import { AuthService } from './auth.service';

/**
 * - Sends auth endpoints with credentials so the HttpOnly refresh cookie flows.
 * - Attaches the Bearer access token to every other API call.
 * - On a 401, renews the token once and retries; if that fails, signs the user out.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const apiBase = inject(API_BASE_URL);

  if (!req.url.startsWith(apiBase)) {
    return next(req);
  }

  if (req.url.startsWith(`${apiBase}/auth/`)) {
    return next(withToken(req.clone({ withCredentials: true }), auth.accessToken));
  }

  return next(withToken(req, auth.accessToken)).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }

      return from(auth.refresh()).pipe(
        switchMap(renewed => {
          if (!renewed) {
            void auth.handleSessionExpired();
            return throwError(() => error);
          }
          return next(withToken(req, auth.accessToken));
        })
      );
    })
  );
};

function withToken(req: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;
}
