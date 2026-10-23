import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiError } from '../../core/models/response-cls.model';
import { JobSourceStatus } from '../jobs/jobs.models';
import { JobsService } from '../jobs/jobs.service';
import { CATALOG, CatalogEntry, IntegrationCategory } from './integration-catalog';
import { IntegrationsService, ProviderStatus } from './integrations.service';
import { OndcService } from './ondc/ondc.service';

export type IntegrationState = 'connected' | 'available' | 'unavailable' | 'soon';

/** A catalog entry plus the signed-in user's connection to it. */
export interface IntegrationItem extends CatalogEntry {
  state: IntegrationState;
  connectedAt: string | null;
  /** Jobs only: connected through the workspace key, so the user can't disconnect it. */
  managedByWorkspace: boolean;
}

export interface Flash {
  kind: 'success' | 'error';
  text: string;
}

/**
 * Connection status for every integration, shared by the integrations nav, list and detail pages so
 * they never disagree and the API is called once per visit (plus explicit refreshes).
 */
@Injectable({ providedIn: 'root' })
export class IntegrationsStore {
  private readonly foodApi = inject(IntegrationsService);
  private readonly jobsApi = inject(JobsService);
  private readonly ondcApi = inject(OndcService);

  private readonly food = signal<ProviderStatus[]>([]);
  private readonly jobs = signal<JobSourceStatus[]>([]);
  /** ONDC needs no per-user connection: it is ready whenever the network is enabled. */
  private readonly ondcEnabled = signal<boolean | null>(null);
  private readonly _loading = signal(false);
  private readonly _loaded = signal(false);
  private readonly _error = signal('');
  private readonly _flash = signal<Flash | null>(null);

  readonly loading = this._loading.asReadonly();
  readonly loaded = this._loaded.asReadonly();
  readonly error = this._error.asReadonly();

  readonly items = computed<IntegrationItem[]>(() =>
    CATALOG.map(entry => {
      if (entry.comingSoon) {
        return { ...entry, state: 'soon', connectedAt: null, managedByWorkspace: false };
      }
      if (entry.id === 'ondc') {
        return { ...entry, state: this.ondcEnabled() ? 'connected' : 'unavailable', connectedAt: null, managedByWorkspace: true };
      }
      if (entry.category === 'food') {
        const status = this.food().find(p => p.provider === entry.id);
        const state: IntegrationState = !status || !status.enabled ? 'unavailable' : status.isConnected ? 'connected' : 'available';
        return { ...entry, name: status?.displayName ?? entry.name, state, connectedAt: status?.connectedAt ?? null, managedByWorkspace: false };
      }
      const source = this.jobs().find(s => s.name === entry.id);
      return {
        ...entry,
        state: source ? (source.connected ? 'connected' : 'available') : 'unavailable',
        connectedAt: source?.connectedAtUtc ?? null,
        managedByWorkspace: !!source?.serverKey
      };
    })
  );

  readonly connectedCount = computed(() => this.items().filter(i => i.state === 'connected').length);

  byCategory(category: IntegrationCategory | 'all'): IntegrationItem[] {
    return category === 'all' ? this.items() : this.items().filter(i => i.category === category);
  }

  find(id: string): IntegrationItem | undefined {
    return this.items().find(i => i.id === id);
  }

  /** Loads once; pass force to refresh after a change. */
  async load(force = false): Promise<void> {
    if ((this._loaded() && !force) || this._loading()) {
      return;
    }
    this._loading.set(true);
    this._error.set('');

    const [food, jobs, ondc] = await Promise.allSettled([this.foodApi.list(), this.jobsApi.sources(), this.ondcApi.status()]);
    if (food.status === 'fulfilled') this.food.set(food.value.statusObject ?? []);
    if (jobs.status === 'fulfilled') this.jobs.set(jobs.value.statusObject ?? []);
    if (ondc.status === 'fulfilled') this.ondcEnabled.set(ondc.value.statusObject?.enabled ?? false);

    const failure = [food, jobs, ondc].find(r => r.status === 'rejected') as PromiseRejectedResult | undefined;
    this._error.set(failure ? ApiError.from(failure.reason).message : '');
    this._loaded.set(true);
    this._loading.set(false);
  }

  /** One-time message carried across a navigation (e.g. the OAuth return). */
  setFlash(flash: Flash): void {
    this._flash.set(flash);
  }

  takeFlash(): Flash | null {
    const flash = this._flash();
    this._flash.set(null);
    return flash;
  }
}
