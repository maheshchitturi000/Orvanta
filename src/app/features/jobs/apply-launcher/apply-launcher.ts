import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, input, output, signal, viewChild } from '@angular/core';
import { ExtensionBridge } from '../../../core/extension/extension-bridge.service';
import { Icon } from '../../../shared/ui/icon/icon';

type Step = 'choose' | 'checking' | 'enable' | 'starting' | 'started' | 'permission' | 'error';

const EXTENSIONS_PAGE = 'chrome://extensions';

/**
 * "Apply" dialog. Applying runs in the Orvanta Chrome extension (in the user's own browser session),
 * so this checks for it first. When it's missing or turned off, it explains how to turn it on and waits;
 * the moment the extension appears, applying starts with the mode chosen here.
 */
@Component({
  selector: 'app-apply-launcher',
  imports: [Icon],
  templateUrl: './apply-launcher.html',
  styleUrl: './apply-launcher.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'close()' }
})
export class ApplyLauncher {
  private readonly bridge = inject(ExtensionBridge);
  private readonly dialog = viewChild<ElementRef<HTMLElement>>('dialog');

  /** Applications ready to submit. */
  readonly ready = input.required<number>();
  readonly closed = output<void>();

  protected readonly step = signal<Step>('choose');
  protected readonly autoSubmit = signal(true);
  protected readonly acknowledged = signal(false);
  protected readonly copied = signal(false);
  protected readonly started = signal(0);
  protected readonly error = signal('');
  protected readonly browserSupported = this.bridge.browserSupported;
  protected readonly extensionsPage = EXTENSIONS_PAGE;

  private waiting: AbortController | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.waiting?.abort());
    queueMicrotask(() => this.dialog()?.nativeElement.focus());
  }

  protected canStart(): boolean {
    return !this.autoSubmit() || this.acknowledged();
  }

  protected async start(): Promise<void> {
    if (!this.canStart()) {
      return;
    }
    this.error.set('');
    this.step.set('checking');

    if (!(await this.bridge.detect())) {
      this.step.set('enable');
      this.waiting = new AbortController();
      try {
        await this.bridge.waitForExtension(this.waiting.signal);
      } catch {
        return; // Dialog closed while waiting.
      }
    }
    await this.launch();
  }

  private async launch(): Promise<void> {
    this.step.set('starting');
    const result = await this.bridge.startApplying(this.autoSubmit());
    if (result.needsPermission) {
      this.step.set('permission');
    } else if (result.started) {
      this.started.set(result.total ?? this.ready());
      this.step.set('started');
    } else {
      this.error.set(result.error || result.message || 'Applying could not start.');
      this.step.set('error');
    }
  }

  protected async copyExtensionsPage(): Promise<void> {
    try {
      await navigator.clipboard.writeText(EXTENSIONS_PAGE);
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    } catch {
      // Clipboard blocked: the address is shown on screen to type.
    }
  }

  protected close(): void {
    this.waiting?.abort();
    this.closed.emit();
  }
}
