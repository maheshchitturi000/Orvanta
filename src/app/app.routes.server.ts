import { RenderMode, ServerRoute } from '@angular/ssr';

export const serverRoutes: ServerRoute[] = [
  {
    // Every page depends on the signed-in user (restored from an HttpOnly cookie
    // in the browser), so render on the client instead of prerendering.
    path: '**',
    renderMode: RenderMode.Client
  }
];
