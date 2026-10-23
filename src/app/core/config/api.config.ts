import { InjectionToken, isDevMode } from '@angular/core';

/**
 * Every request goes through the Orvanta Gateway.
 * Development: `ng serve` on :4200 talks to the Gateway on :5095.
 * Production: the Gateway serves this app too, so the API is on the same origin (the refresh cookie is SameSite=Strict).
 */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  providedIn: 'root',
  factory: () => (isDevMode() ? 'http://localhost:5095/api' : '/api')
});
