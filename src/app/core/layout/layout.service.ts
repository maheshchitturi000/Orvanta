import { Injectable, signal } from '@angular/core';

/** UI state shared by the shell, sidebar and top navbar (mobile drawer). */
@Injectable({ providedIn: 'root' })
export class LayoutService {
  private readonly _sidebarOpen = signal(false);
  readonly sidebarOpen = this._sidebarOpen.asReadonly();

  openSidebar(): void {
    this._sidebarOpen.set(true);
  }

  closeSidebar(): void {
    this._sidebarOpen.set(false);
  }
}
