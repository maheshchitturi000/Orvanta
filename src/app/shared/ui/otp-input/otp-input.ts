import { ChangeDetectionStrategy, Component, ElementRef, computed, input, model, output, viewChildren } from '@angular/core';

/**
 * One box per digit. Supports typing, paste and SMS/email autofill anywhere in the row, Backspace to
 * the previous box and arrow keys. Emits `completed` once every box is filled.
 */
@Component({
  selector: 'app-otp-input',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { role: 'group', '[attr.aria-label]': 'length() + "-digit code"', '(paste)': 'onPaste($event)' },
  template: `
    @for (digit of digits(); track $index) {
      <input #box class="otp-box" type="text" inputmode="numeric" autocomplete="one-time-code" maxlength="6"
             [attr.aria-label]="'Digit ' + ($index + 1)" [value]="digit" [class.filled]="!!digit" [class.invalid]="invalid()"
             [disabled]="disabled()" (input)="onInput($index, $event)" (keydown)="onKeydown($index, $event)" />
    }
  `,
  styles: `
    :host {
      display: grid;
      grid-template-columns: repeat(var(--count, 6), 1fr);
      gap: 10px;
    }

    .otp-box {
      width: 100%;
      aspect-ratio: 1 / 1.1;
      max-height: 64px;
      border-radius: var(--radius);
      border: 1.5px solid var(--border-strong);
      background: var(--surface);
      color: var(--lavender-900);
      font: inherit;
      font-size: 24px;
      font-weight: 700;
      text-align: center;
      transition: border-color 0.15s ease, box-shadow 0.15s ease, background 0.15s ease;

      &:focus {
        outline: none;
        border-color: var(--lavender-500);
        box-shadow: var(--focus-ring);
      }

      &.filled {
        border-color: var(--lavender-300);
        background: var(--lavender-50);
      }

      &.invalid {
        border-color: var(--danger);
      }

      &:disabled {
        opacity: 0.7;
      }
    }
  `
})
export class OtpInput {
  readonly length = input(6);
  readonly disabled = input(false);
  readonly invalid = input(false);
  /** Digits by position; an empty box is a space, so clearing a middle box never shifts the others. */
  readonly value = model('');
  readonly completed = output<string>();

  private readonly boxes = viewChildren<ElementRef<HTMLInputElement>>('box');

  protected readonly digits = computed(() =>
    Array.from({ length: this.length() }, (_, i) => (/\d/.test(this.value()[i] ?? '') ? this.value()[i] : ''))
  );

  /** Empties every box and puts the cursor in the first one (e.g. after a wrong code). */
  clear(): void {
    this.value.set('');
    this.boxes().forEach(b => (b.nativeElement.value = ''));
    this.focus(0);
  }

  focus(index = 0): void {
    setTimeout(() => this.boxes()[index]?.nativeElement.focus());
  }

  protected onInput(index: number, event: Event): void {
    const input = event.target as HTMLInputElement;
    const typed = input.value.replace(/\D/g, '');
    if (typed.length > 1) {
      // Typed fast or autofilled: spread across the boxes.
      this.fillFrom(index, typed);
      return;
    }
    input.value = typed;
    this.setDigit(index, typed);
    if (typed && index < this.length() - 1) {
      this.focus(index + 1);
    }
    this.emitIfComplete();
  }

  protected onKeydown(index: number, event: KeyboardEvent): void {
    if (event.key === 'Backspace' && !this.digits()[index] && index > 0) {
      this.setDigit(index - 1, '');
      this.focus(index - 1);
      event.preventDefault();
    } else if (event.key === 'ArrowLeft' && index > 0) {
      this.focus(index - 1);
    } else if (event.key === 'ArrowRight' && index < this.length() - 1) {
      this.focus(index + 1);
    }
  }

  protected onPaste(event: ClipboardEvent): void {
    const pasted = event.clipboardData?.getData('text').replace(/\D/g, '') ?? '';
    if (pasted && !this.disabled()) {
      event.preventDefault();
      this.fillFrom(0, pasted);
    }
  }

  private fillFrom(start: number, typed: string): void {
    const next = [...this.digits()];
    for (let i = 0; i < typed.length && start + i < this.length(); i++) {
      next[start + i] = typed[i];
    }
    this.value.set(toValue(next));
    this.boxes().forEach((b, i) => (b.nativeElement.value = next[i]));
    this.focus(Math.min(start + typed.length, this.length() - 1));
    this.emitIfComplete();
  }

  private setDigit(index: number, digit: string): void {
    const next = [...this.digits()];
    next[index] = digit;
    this.value.set(toValue(next));
  }

  private emitIfComplete(): void {
    const code = this.value();
    if (new RegExp(`^\\d{${this.length()}}$`).test(code)) {
      this.completed.emit(code);
    }
  }
}

function toValue(digits: string[]): string {
  return digits.map(d => d || ' ').join('').trimEnd();
}
