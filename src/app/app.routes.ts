import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth/auth.guards';

export const routes: Routes = [
  // Public: account access
  {
    path: 'login',
    canActivate: [guestGuard],
    title: 'Sign in · Orvanta AI',
    loadComponent: () => import('./features/auth/login/login').then(m => m.Login)
  },
  {
    path: 'register',
    canActivate: [guestGuard],
    title: 'Create account · Orvanta AI',
    loadComponent: () => import('./features/auth/register/register').then(m => m.Register)
  },
  {
    path: 'verify-email',
    canActivate: [guestGuard],
    title: 'Verify email · Orvanta AI',
    loadComponent: () => import('./features/auth/verify-email/verify-email').then(m => m.VerifyEmail)
  },

  // Signed-in only
  {
    path: '',
    canActivate: [authGuard],
    title: 'Orvanta AI',
    loadComponent: () => import('./features/chat/chat').then(m => m.Chat)
  },
  {
    path: 'integrations',
    canActivate: [authGuard],
    title: 'Integrations · Orvanta AI',
    loadComponent: () => import('./features/integrations/integrations').then(m => m.Integrations)
  },

  { path: '**', redirectTo: '' }
];
