import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '../../../core/models/response-cls.model';
import { Icon } from '../../../shared/ui/icon/icon';
import { IntegrationLogo } from '../integration-logo/integration-logo';
import { IntegrationsService, ProviderAddress } from '../integrations.service';
import { Flash, IntegrationsStore } from '../integrations.store';

/** /integrations/food/:provider — everything about one food app: connection, addresses, capabilities. */
@Component({
  selector: 'app-provider-detail',
  imports: [DatePipe, RouterLink, Icon, IntegrationLogo],
  templateUrl: './provider-detail.html',
  styleUrl: '../integration-detail.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ProviderDetail {
  private readonly api = inject(IntegrationsService);
  protected readonly store = inject(IntegrationsStore);

  /** Route parameter. */
  readonly provider = input.required<string>();

  protected readonly item = computed(() => {
    const item = this.store.find(this.provider());
    return item?.category === 'food' ? item : undefined;
  });
  protected readonly connected = computed(() => this.item()?.state === 'connected');

  protected readonly flash = signal<Flash | null>(this.store.takeFlash());
  protected readonly busy = signal(false);
  protected readonly confirmingDisconnect = signal(false);

  protected readonly addresses = signal<ProviderAddress[]>([]);
  protected readonly addressesLoading = signal(false);
  protected readonly addressesError = signal('');
  protected readonly addressSource = signal('');

  constructor() {
    void this.store.load();

    // Load addresses whenever this page shows a connected provider (also after switching providers).
    effect(() => {
      const provider = this.provider();
      const connected = this.connected();
      untracked(() => {
        this.confirmingDisconnect.set(false);
        this.addresses.set([]);
        if (connected) {
          void this.loadAddresses(provider, false);
        }
      });
    });
  }

  protected async connect(): Promise<void> {
    this.busy.set(true);
    this.flash.set(null);
    try {
      const response = await this.api.connect(this.provider());
      const url = response.statusObject?.authorizationUrl;
      if (!url) {
        throw new ApiError(500, 'No authorization link was returned.');
      }
      // Leaves the app for the provider's consent page; FoodService brings the user back here.
      window.location.assign(url);
    } catch (error) {
      this.flash.set({ kind: 'error', text: ApiError.from(error).message });
      this.busy.set(false);
    }
  }

  protected async disconnect(): Promise<void> {
    const name = this.item()?.name ?? this.provider();
    this.busy.set(true);
    this.flash.set(null);
    try {
      await this.api.disconnect(this.provider());
      this.confirmingDisconnect.set(false);
      this.flash.set({ kind: 'success', text: `${name} disconnected. You can connect it again at any time.` });
      await this.store.load(true);
    } catch (error) {
      this.flash.set({ kind: 'error', text: ApiError.from(error).message });
    } finally {
      this.busy.set(false);
    }
  }

  protected async loadAddresses(provider: string, refresh: boolean): Promise<void> {
    this.addressesLoading.set(true);
    this.addressesError.set('');
    try {
      const response = await this.api.addresses(provider, refresh);
      this.addresses.set(response.statusObject?.addresses ?? []);
      this.addressSource.set(response.statusObject?.source ?? '');
    } catch (error) {
      this.addressesError.set(ApiError.from(error).message);
    } finally {
      this.addressesLoading.set(false);
    }
  }

  protected async makeDefault(address: ProviderAddress): Promise<void> {
    this.addressesError.set('');
    try {
      await this.api.setDefaultAddress(this.provider(), address.id);
      this.addresses.update(all => all.map(a => ({ ...a, isDefault: a.id === address.id })));
    } catch (error) {
      this.addressesError.set(ApiError.from(error).message);
    }
  }
}
