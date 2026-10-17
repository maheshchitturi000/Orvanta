import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth/auth.guards';
import { AppShell } from './core/layout/app-shell/app-shell';
import { PageMeta } from './core/layout/topbar/topbar';

const assistant: PageMeta = {
  title: 'AI Assistant',
  subtitle: 'Ask anything, or order food from Swiggy and Zomato',
  icon: 'sparkles'
};

const jobs: PageMeta = {
  title: 'Jobs',
  subtitle: 'Daily matches from company career boards · approve once, Orvanta prepares every application',
  icon: 'briefcase'
};

const integrations: PageMeta = {
  title: 'Integrations',
  subtitle: 'Connect the apps Orvanta can use on your behalf',
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
        title: 'Integrations · Orvanta AI',
        data: integrations,
        loadComponent: () => import('./features/integrations/integrations').then(m => m.Integrations)
      }
    ]
  },

  { path: '**', redirectTo: '' }
];
