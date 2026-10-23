import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, computed, effect, inject, signal } from '@angular/core';

export type ThemePreference = 'light' | 'dark' | 'system';
export type Theme = 'light' | 'dark';

/** Must match the inline script in index.html, which applies the theme before Angular starts. */
const STORAGE_KEY = 'orv_theme';
const THEME_COLORS: Record<Theme, string> = { light: '#7c5cff', dark: '#17141f' };

/**
 * Light / Dark / System appearance. The choice is remembered per browser; "System" follows the
 * OS setting live. Applies <html data-theme="light|dark">, which switches the CSS tokens in styles.scss.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));
  private readonly media =
    this.isBrowser && typeof window.matchMedia === 'function' ? window.matchMedia('(prefers-color-scheme: dark)') : null;
  private readonly systemDark = signal(this.media?.matches ?? false);
  private transitionTimer: ReturnType<typeof setTimeout> | null = null;

  private readonly _preference = signal<ThemePreference>(this.readStored());
  readonly preference = this._preference.asReadonly();
  readonly theme = computed<Theme>(() => {
    const preference = this._preference();
    return preference === 'system' ? (this.systemDark() ? 'dark' : 'light') : preference;
  });

  constructor() {
    this.media?.addEventListener('change', event => this.systemDark.set(event.matches));
    effect(() => this.apply(this.theme()));
  }

  setPreference(preference: ThemePreference): void {
    if (preference === this._preference()) {
      return;
    }

    const change = () => {
      this._preference.set(preference);
      // Applied right away (not via the effect) so the view transition captures the new look.
      this.apply(this.theme());
    };

    const doc = this.document as Document & { startViewTransition?: (update: () => void) => { finished: Promise<void> } };
    if (this.motionAllowed() && typeof doc.startViewTransition === 'function') {
      // Whole-screen cross-fade, including gradients and shadows that CSS transitions can't animate.
      const root = this.document.documentElement;
      root.classList.add('theme-switching');
      doc.startViewTransition(change).finished.finally(() => root.classList.remove('theme-switching'));
    } else {
      this.animateNextChange();
      change();
    }

    try {
      localStorage.setItem(STORAGE_KEY, preference);
    } catch {
      // Private mode / blocked storage: the choice still applies for this visit.
    }
  }

  private motionAllowed(): boolean {
    return !!this.media && !window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  }

  private apply(theme: Theme): void {
    // On the server the theme is unknown; the inline script in index.html sets it before first paint.
    if (!this.isBrowser) {
      return;
    }
    this.document.documentElement.setAttribute('data-theme', theme);
    this.document.querySelector('meta[name="theme-color"]')?.setAttribute('content', THEME_COLORS[theme]);
  }

  /** Fallback for browsers without view transitions: ease colors for a user-initiated switch. */
  private animateNextChange(): void {
    if (!this.motionAllowed()) {
      return;
    }
    const root = this.document.documentElement;
    root.classList.add('theme-transition');
    if (this.transitionTimer) {
      clearTimeout(this.transitionTimer);
    }
    this.transitionTimer = setTimeout(() => root.classList.remove('theme-transition'), 300);
  }

  private readStored(): ThemePreference {
    if (!this.isBrowser) {
      return 'system';
    }
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      return stored === 'light' || stored === 'dark' || stored === 'system' ? stored : 'system';
    } catch {
      return 'system';
    }
  }
}
