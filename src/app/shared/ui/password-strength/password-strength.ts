import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { passwordScore } from '../../validators/form.validators';

/** Strength meter + live checklist for a new password (same rules as the server's password policy). */
@Component({
  selector: 'app-password-strength',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="strength" [attr.data-score]="score()">
      <div class="strength-bars" aria-hidden="true">
        <span></span><span></span><span></span><span></span>
      </div>
      <span class="strength-label">{{ label() }}</span>
    </div>

    <ul class="rules" aria-label="Password requirements">
      @for (rule of rules(); track rule.label) {
        <li [class.ok]="rule.ok">
          <span class="rule-icon" aria-hidden="true">{{ rule.ok ? '✓' : '•' }}</span>{{ rule.label }}
          <span class="sr-only">{{ rule.ok ? '(met)' : '(not met)' }}</span>
        </li>
      }
    </ul>
  `,
  styles: `
    :host {
      display: grid;
      gap: 6px;
      margin-top: 4px;
    }

    .strength {
      display: flex;
      align-items: center;
      gap: 10px;

      .strength-bars {
        display: grid;
        grid-template-columns: repeat(4, 1fr);
        gap: 4px;
        flex: 1;

        span {
          height: 4px;
          border-radius: 4px;
          background: var(--lavender-100);
          transition: background 0.2s ease;
        }
      }

      .strength-label {
        min-width: 64px;
        font-size: 12px;
        font-weight: 600;
        color: var(--text-subtle);
        text-align: right;
      }

      &[data-score='1'] .strength-bars span:nth-child(-n + 1) { background: #f08a8a; }
      &[data-score='2'] .strength-bars span:nth-child(-n + 2) { background: #f0b45c; }
      &[data-score='3'] .strength-bars span:nth-child(-n + 3) { background: var(--lavender-400); }
      &[data-score='4'] .strength-bars span { background: var(--lavender-600); }
    }

    .rules {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 4px 12px;
      margin: 0;
      padding: 0;
      list-style: none;

      li {
        display: flex;
        align-items: center;
        gap: 6px;
        font-size: 12px;
        color: var(--text-subtle);

        &.ok {
          color: var(--success);
        }
      }

      .rule-icon {
        width: 12px;
        text-align: center;
      }
    }
  `
})
export class PasswordStrength {
  readonly value = input('');

  protected readonly score = computed(() => passwordScore(this.value()));
  protected readonly label = computed(() => ['Too weak', 'Weak', 'Fair', 'Good', 'Strong'][this.score()]);
  protected readonly rules = computed(() => {
    const value = this.value();
    return [
      { label: '8+ characters', ok: value.length >= 8 },
      { label: 'Uppercase & lowercase', ok: /[A-Z]/.test(value) && /[a-z]/.test(value) },
      { label: 'A number', ok: /\d/.test(value) },
      { label: 'A symbol', ok: /[^A-Za-z\d]/.test(value) }
    ];
  });
}
