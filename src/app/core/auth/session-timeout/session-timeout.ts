import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { Icon } from '../../../shared/ui/icon/icon';
import { AuthService, SESSION_WARNING_LEAD_SECONDS } from '../auth.service';

const RING_RADIUS = 34;
const RING_LENGTH = 2 * Math.PI * RING_RADIUS;

/**
 * "Stay signed in?" prompt shown shortly before the access token expires. Continuing renews it with
 * the refresh token; ignoring it lets the session end (AuthService signs the user out at expiry).
 */
@Component({
  selector: 'app-session-timeout',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (auth.sessionWarning()) {
      <div class="scrim" aria-hidden="true"></div>
      <div class="dialog" role="alertdialog" aria-modal="true" aria-labelledby="session-title" aria-describedby="session-desc">
        <div class="ring" aria-hidden="true">
          <svg viewBox="0 0 80 80" width="80" height="80">
            <circle class="track" cx="40" cy="40" [attr.r]="radius" />
            <circle class="progress" cx="40" cy="40" [attr.r]="radius"
                    [attr.stroke-dasharray]="ringLength" [attr.stroke-dashoffset]="dashOffset()" />
          </svg>
          <span class="time">{{ countdown() }}</span>
        </div>

        <h2 id="session-title">Your session is about to expire</h2>
        <p id="session-desc">
          For your security, you'll be signed out in <strong>{{ countdown() }}</strong>.
          Do you want to stay signed in?
        </p>

        @if (error()) {
          <div class="alert error" role="alert"><app-icon name="alert" [size]="16" /> {{ error() }}</div>
        }

        <div class="actions">
          <button type="button" class="btn btn-ghost" (click)="signOut()" [disabled]="busy()">Sign out</button>
          <button #stayButton type="button" class="btn btn-primary" (click)="stay()" [disabled]="busy()">
            @if (busy()) { <span class="spinner"></span> Renewing… } @else { Stay signed in }
          </button>
        </div>
        <small class="note"><app-icon name="lock" [size]="12" /> Your work on this page is kept.</small>
      </div>
    }
  `,
  styles: `
    .scrim {
      position: fixed;
      inset: 0;
      z-index: 90;
      background: var(--overlay);
      backdrop-filter: blur(3px);
      animation: fade 0.2s ease;
    }

    .dialog {
      position: fixed;
      z-index: 91;
      top: 50%;
      left: 50%;
      width: min(420px, calc(100vw - 32px));
      transform: translate(-50%, -50%);
      display: grid;
      justify-items: center;
      gap: 12px;
      padding: 28px 26px 20px;
      border: 1px solid var(--border);
      border-radius: var(--radius-lg);
      background: var(--surface);
      box-shadow: var(--shadow-lg);
      text-align: center;
      animation: pop 0.22s cubic-bezier(0.2, 0.9, 0.3, 1.2);
    }

    .ring {
      position: relative;
      width: 80px;
      height: 80px;

      svg {
        transform: rotate(-90deg);
      }

      circle {
        fill: none;
        stroke-width: 6;
      }

      .track {
        stroke: var(--lavender-100);
      }

      .progress {
        stroke: var(--accent);
        stroke-linecap: round;
        transition: stroke-dashoffset 1s linear;
      }

      .time {
        position: absolute;
        inset: 0;
        display: grid;
        place-items: center;
        font-size: 17px;
        font-weight: 700;
        font-variant-numeric: tabular-nums;
        color: var(--text);
      }
    }

    h2 {
      font-size: 18px;
    }

    p {
      font-size: 14px;
      color: var(--text-muted);
    }

    .alert {
      width: 100%;
      align-items: center;
      text-align: left;
    }

    .actions {
      display: grid;
      grid-template-columns: 1fr 1.4fr;
      gap: 10px;
      width: 100%;
      margin-top: 6px;
    }

    .note {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      font-size: 12px;
      color: var(--text-subtle);
    }

    @keyframes fade {
      from { opacity: 0; }
    }

    @keyframes pop {
      from { opacity: 0; transform: translate(-50%, -46%) scale(0.96); }
    }
  `
})
export class SessionTimeout {
  protected readonly auth = inject(AuthService);
  private readonly stayButton = viewChild<ElementRef<HTMLButtonElement>>('stayButton');

  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly radius = RING_RADIUS;
  protected readonly ringLength = RING_LENGTH;

  protected readonly countdown = computed(() => {
    const seconds = this.auth.secondsLeft();
    return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
  });

  protected readonly dashOffset = computed(() =>
    RING_LENGTH * (1 - Math.min(this.auth.secondsLeft() / SESSION_WARNING_LEAD_SECONDS, 1))
  );

  constructor() {
    // Focus the safe default as soon as the prompt opens, so Enter keeps the user signed in.
    effect(() => {
      if (this.auth.sessionWarning()) {
        this.error.set('');
        queueMicrotask(() => this.stayButton()?.nativeElement.focus());
      }
    });
  }

  protected async stay(): Promise<void> {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      if (!(await this.auth.continueSession())) {
        this.error.set('Your session could not be renewed. Please sign in again.');
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected signOut(): void {
    void this.auth.logout();
  }
}
