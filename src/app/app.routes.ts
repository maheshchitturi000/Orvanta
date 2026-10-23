import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth/auth.guards';
import { AppShell } from './core/layout/app-shell/app-shell';
import { PageMeta } from './core/layout/topbar/topbar';

const dashboard: PageMeta = {
  title: 'Dashboard',
  icon: 'dashboard'
};

const assistant: PageMeta = {
  title: 'AI Assistant',
  icon: 'sparkles'
};

const jobs: PageMeta = {
  title: 'Jobs',
  icon: 'briefcase'
};

const integrations: PageMeta = {
  title: 'Integrations',
  icon: 'apps',
  hideConnect: true
};

export const routes: Routes = [
  // Public: account access (full-screen auth layout)
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
    path: 'forgot-password',
    canActivate: [guestGuard],
    title: 'Reset password · Orvanta AI',
    loadComponent: () => import('./features/auth/forgot-password/forgot-password').then(m => m.ForgotPassword)
  },
  {
    path: 'verify-email',
    canActivate: [guestGuard],
    title: 'Verify email · Orvanta AI',
    loadComponent: () => import('./features/auth/verify-email/verify-email').then(m => m.VerifyEmail)
  },

  // Signed-in: every page renders inside the shared shell (sidebar + top navbar)
  {
    path: '',
    component: AppShell,
    canActivate: [authGuard],
    children: [
      {
        path: '',
        title: 'Orvanta AI',
        data: assistant,
        loadComponent: () => import('./features/chat/chat').then(m => m.Chat)
      },
      {
        path: 'dashboard',
        title: 'Dashboard · Orvanta AI',
        data: dashboard,
        loadComponent: () => import('./features/dashboard/dashboard').then(m => m.Dashboard)
      },
      {
        path: 'c/:conversationId',
        title: 'Conversation · Orvanta AI',
        data: assistant,
        loadComponent: () => import('./features/chat/chat').then(m => m.Chat)
      },
      {
        path: 'jobs',
        data: jobs,
        loadComponent: () => import('./features/jobs/jobs-layout/jobs-layout').then(m => m.JobsLayout),
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'matches' },
          {
            path: 'matches',
            title: 'Job matches · Orvanta AI',
            data: jobs,
            loadComponent: () => import('./features/jobs/matches/matches').then(m => m.Matches)
          },
          {
            path: 'applications',
            title: 'Applications · Orvanta AI',
            data: jobs,
            loadComponent: () => import('./features/jobs/applications/applications').then(m => m.Applications)
          },
          {
            path: 'profile',
            title: 'CV & preferences · Orvanta AI',
            data: jobs,
            loadComponent: () => import('./features/jobs/profile/job-profile').then(m => m.JobProfile)
          }
        ]
      },
      {
        path: 'integrations',
        data: integrations,
        loadComponent: () =>
          import('./features/integrations/integrations-layout/integrations-layout').then(m => m.IntegrationsLayout),
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'all' },
          ...(['all', 'food', 'jobs', 'travel'] as const).map(category => ({
            path: category,
            pathMatch: 'full' as const,
            title: `${category === 'all' ? 'Integrations' : category[0].toUpperCase() + category.slice(1)} · Orvanta AI`,
            data: { ...integrations, category },
            loadComponent: () =>
              import('./features/integrations/integration-list/integration-list').then(m => m.IntegrationList)
          })),
          {
            path: 'food/ondc',
            title: 'ONDC · Orvanta AI',
            data: integrations,
            loadComponent: () => import('./features/integrations/ondc/ondc-detail').then(m => m.OndcDetail)
          },
          {
            path: 'food/:provider',
            title: 'Food app · Orvanta AI',
            data: integrations,
            loadComponent: () =>
              import('./features/integrations/provider-detail/provider-detail').then(m => m.ProviderDetail)
          },
          {
            path: 'jobs/:source',
            title: 'Job source · Orvanta AI',
            data: integrations,
            loadComponent: () =>
              import('./features/integrations/job-source-detail/job-source-detail').then(m => m.JobSourceDetail)
          },
          { path: '**', redirectTo: 'all' }
        ]
      }
    ]
  },

  { path: '**', redirectTo: '' }
];
