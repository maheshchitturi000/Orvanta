import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { LayoutService } from '../layout.service';
import { Sidebar } from '../sidebar/sidebar';
import { Topbar } from '../topbar/topbar';

/**
 * Frame for every signed-in page: sidebar + top navbar + routed content.
 * Pages render inside <router-outlet> and never draw their own chrome.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, Sidebar, Topbar],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.sidebar-open]': 'layout.sidebarOpen()' },
  template: `
    <app-sidebar class="sidebar" />
    <div class="backdrop" (click)="layout.closeSidebar()" aria-hidden="true"></div>
    <div class="main">
      <app-topbar />
      <main class="content">
        <router-outlet />
      </main>
    </div>
  `,
  styleUrl: './app-shell.scss'
})
export class AppShell {
  protected readonly layout = inject(LayoutService);
}
