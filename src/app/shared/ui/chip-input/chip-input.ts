import { ChangeDetectionStrategy, Component, input, model, signal } from '@angular/core';
import { Icon } from '../icon/icon';

/** Editable list of short values ("chips"). Enter or comma adds, Backspace on empty removes the last. */
@Component({
  selector: 'app-chip-input',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="chips" (click)="box.focus()">
      @for (value of values(); track value) {
        <span class="chip">
          {{ value }}
          <button type="button" (click)="remove(value); $event.stopPropagation()" [attr.aria-label]="'Remove ' + value">
            <app-icon name="x" [size]="12" [strokeWidth]="2.5" />
          </button>
        </span>
      }
      <input #box [id]="inputId()" [placeholder]="values().length ? '' : placeholder()" [value]="draft()"
             (input)="draft.set(box.value)" (keydown)="onKey($event)" (blur)="commit()" [attr.aria-label]="label()" />
    </div>
    @if (suggestions().length) {
      <div class="suggestions">
        @for (s of suggestions(); track s) {
          @if (!has(s)) {
            <button type="button" (click)="add(s)">+ {{ s }}</button>
          }
        }
      </div>
    }
  `,
  styles: `
    :host { display: grid; gap: 8px; }
    .chips {
      display: flex; flex-wrap: wrap; gap: 6px; align-items: center;
      min-height: 46px; padding: 6px 8px; cursor: text;
      border: 1px solid var(--border-strong); border-radius: var(--radius-sm); background: var(--white);
      transition: border-color .15s ease, box-shadow .15s ease;
    }
    .chips:focus-within { border-color: var(--lavender-500); box-shadow: var(--focus-ring); }
    .chip {
      display: inline-flex; align-items: center; gap: 4px; height: 28px; padding: 0 6px 0 10px;
      border-radius: 999px; background: var(--lavender-100); color: var(--lavender-800); font-size: 13px; font-weight: 500;
    }
    .chip button {
      display: grid; place-items: center; width: 18px; height: 18px; border: 0; border-radius: 50%;
      background: transparent; color: var(--lavender-700); cursor: pointer;
    }
    .chip button:hover { background: var(--lavender-200); }
    input { flex: 1; min-width: 140px; height: 30px; border: 0; outline: none; font: inherit; background: transparent; color: var(--text); }
    .suggestions { display: flex; flex-wrap: wrap; gap: 6px; }
    .suggestions button {
      height: 26px; padding: 0 10px; border: 1px dashed var(--lavender-300); border-radius: 999px;
      background: var(--white); color: var(--lavender-700); font: inherit; font-size: 12px; cursor: pointer;
    }
    .suggestions button:hover { background: var(--lavender-50); }
  `
})
export class ChipInput {
  readonly values = model<string[]>([]);
  readonly placeholder = input('Type and press Enter');
  readonly label = input('Add item');
  readonly inputId = input<string>();
  readonly suggestions = input<string[]>([]);
  readonly max = input(60);

  protected readonly draft = signal('');

  protected has(value: string): boolean {
    return this.values().some(v => v.toLowerCase() === value.toLowerCase());
  }

  protected onKey(event: KeyboardEvent): void {
    if (event.key === 'Enter' || event.key === ',') {
      event.preventDefault();
      this.commit();
    } else if (event.key === 'Backspace' && !this.draft() && this.values().length) {
      this.values.update(list => list.slice(0, -1));
    }
  }

  protected commit(): void {
    const value = this.draft().trim().replace(/,$/, '');
    if (value) this.add(value);
    this.draft.set('');
  }

  protected add(value: string): void {
    if (!this.has(value) && this.values().length < this.max()) {
      this.values.update(list => [...list, value]);
    }
  }

  protected remove(value: string): void {
    this.values.update(list => list.filter(v => v !== value));
  }
}
