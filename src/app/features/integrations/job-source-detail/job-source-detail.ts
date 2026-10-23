import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, input, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '../../../core/models/response-cls.model';
import { Icon } from '../../../shared/ui/icon/icon';
import { JobsService } from '../../jobs/jobs.service';
import { IntegrationLogo } from '../integration-logo/integration-logo';
import { Flash, IntegrationsStore } from '../integrations.store';
import { JOB_SOURCE_GUIDES } from './job-source-guides';

/** /integrations/jobs/:source — connect a job search source (JSearch, Adzuna) with the provider's API key. */
@Component({
  selector: 'app-job-source-detail',
  imports: [DatePipe, FormsModule, RouterLink, Icon, IntegrationLogo],
  templateUrl: './job-source-detail.html',
  styleUrls: ['../integration-detail.scss', './job-source-detail.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class JobSourceDetail {
  private readonly api = inject(JobsService);
  protected readonly store = inject(IntegrationsStore);
  /** The first key field (viewChild returns the first match). */
  private readonly keyInput = viewChild<ElementRef<HTMLInputElement>>('keyInput');

  /** Route parameter. */
  readonly source = input.required<string>();

  protected readonly item = computed(() => {
    const item = this.store.find(this.source());
    return item?.category === 'jobs' ? item : undefined;
  });
  protected readonly connected = computed(() => this.item()?.state === 'connected');
  protected readonly guide = computed(() => JOB_SOURCE_GUIDES[this.source()] ?? JOB_SOURCE_GUIDES['jsearch']);

  protected readonly flash = signal<Flash | null>(this.store.takeFlash());
  protected readonly connecting = signal(false);
  protected readonly confirmingDisconnect = signal(false);
  protected readonly busy = signal(false);
  protected readonly values = signal<Partial<Record<string, string>>>({});
  protected readonly complete = computed(() => this.guide().fields.every(f => (this.values()[f.id] ?? '').trim().length > 0));
  protected readonly showKey = signal(false);
  protected readonly keyError = signal('');

  constructor() {
    void this.store.load();
  }

  /** Opens the provider's sign-up page in a new tab and shows the paste-key form here. */
  protected startConnect(): void {
    window.open(this.guide().signUpUrl, '_blank', 'noopener');
    this.connecting.set(true);
    this.values.set({});
    this.keyError.set('');
    this.flash.set(null);
    setTimeout(() => {
      const input = this.keyInput()?.nativeElement;
      input?.scrollIntoView({ behavior: 'smooth', block: 'center' });
      input?.focus();
    });
  }

  protected cancel(): void {
    this.connecting.set(false);
    this.keyError.set('');
  }

  protected setValue(field: string, value: string): void {
    this.values.update(v => ({ ...v, [field]: value }));
  }

  protected async connect(): Promise<void> {
    const parts = this.guide().fields.map(f => (this.values()[f.id] ?? '').trim());
    const missing = this.guide().fields.find((_, i) => parts[i].length < 4);
    if (missing) {
      this.keyError.set(`Paste the full ${missing.label}.`);
      return;
    }
    // Sources with two credentials (Adzuna: application id + key) are stored as "id:key".
    const key = parts.join(':');

    this.busy.set(true);
    this.keyError.set('');
    try {
      const response = await this.api.connectSource(this.source(), key);
      this.flash.set({ kind: 'success', text: response.statusMessage });
      this.connecting.set(false);
      this.values.set({});
      await this.store.load(true);
    } catch (error) {
      this.keyError.set(ApiError.from(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected async disconnect(): Promise<void> {
    const name = this.item()?.name ?? this.source();
    this.busy.set(true);
    this.flash.set(null);
    try {
      await this.api.disconnectSource(this.source());
      this.confirmingDisconnect.set(false);
      this.flash.set({ kind: 'success', text: `${name} disconnected.` });
      await this.store.load(true);
    } catch (error) {
      this.flash.set({ kind: 'error', text: ApiError.from(error).message });
    } finally {
      this.busy.set(false);
    }
  }
}
