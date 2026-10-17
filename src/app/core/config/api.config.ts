import { InjectionToken } from '@angular/core';

/** Every request goes through the Orvanta Gateway. */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  providedIn: 'root',
  factory: () => 'http://localhost:5095/api'
});
