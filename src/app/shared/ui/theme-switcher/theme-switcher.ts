import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { ThemePreference, ThemeService } from '../../../core/theme/theme.service';
import { Icon, IconName } from '../icon/icon';

interface ThemeOption {
  value: ThemePreference;
  label: string;
  icon: IconName;
}

/** Light / Dark / System segmented control. `compact` shows icons only (top navbar). */
@Component({
  selector: 'app-theme-switcher',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { role: 'radiogroup', 'aria-label': 'Appearance', '[class.compact]': 'compact()' },
  template: `
    @for (option of options; track option.value) {
      <button type="button" role="radio" [attr.aria-checked]="theme.preference() === option.value"
              [class.active]="theme.preference() === option.value" [title]="option.label + ' theme'"
              (click)="theme.setPreference(option.value)">
        <app-icon [name]="option.icon" [size]="15" />
        @if (!compact()) {
          <span>{{ option.label }}</span>
        } @else {
          <span class="sr-only">{{ option.label }}</span>
        }
      </button>
    }
  `,
  styles: `
    :host {
      display: inline-flex;
      gap: 2px;
      padding: 3px;
      border: 1px solid var(--border);
      border-radius: 11px;
      background: var(--lavender-25);
    }

    :host(:not(.compact)) {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
    }

    button {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      gap: 6px;
      height: 30px;
      padding: 0 10px;
      border: 0;
      border-radius: 8px;
      background: transparent;
      color: var(--text-muted);
      font: inherit;
      font-size: 12.5px;
      font-weight: 600;
      cursor: pointer;
      transition: background 0.15s ease, color 0.15s ease;

      &:hover:not(.active) {
        color: var(--lavender-700);
      }

      &.active {
        background: var(--surface);
        color: var(--lavender-700);
        box-shadow: var(--shadow-sm), 0 0 0 1px var(--border);
      }
    }

    :host(.compact) button {
      width: 30px;
      padding: 0;
    }
  `
})
export class ThemeSwitcher {
  protected readonly theme = inject(ThemeService);
  readonly compact = input(false);

  protected readonly options: ThemeOption[] = [
    { value: 'light', label: 'Light', icon: 'sun' },
    { value: 'dark', label: 'Dark', icon: 'moon' },
    { value: 'system', label: 'System', icon: 'monitor' }
  ];
}
