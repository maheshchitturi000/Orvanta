import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiError } from '../../core/models/response-cls.model';
import { IntegrationsService, ProviderAddress, ProviderStatus } from './integrations.service';

interface AddressPanel {
  open: boolean;
  loading: boolean;
  error: string;
  source: string;
  items: ProviderAddress[];
}

const PROVIDER_META: Record<string, { letter: string; blurb: string }> = {
  swiggy: { letter: 'S', blurb: 'Search dishes and place cash-on-delivery orders through Swiggy’s official MCP API.' },
  zomato: { letter: 'Z', blurb: 'Search Zomato restaurants and dishes alongside Swiggy for better top picks.' }
};

@Component({
  selector: 'app-integrations',
  imports: [DatePipe, RouterLink],
  templateUrl: './integrations.html',
  styleUrl: './integrations.scss'
})
export class Integrations implements OnInit {
  private readonly api = inject(IntegrationsService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly providers = signal<ProviderStatus[]>([]);
  readonly loading = signal(true);
  readonly busy = signal<string | null>(null);
  readonly error = signal('');
  readonly success = signal('');
  readonly panels = signal<Record<string, AddressPanel>>({});

  ngOnInit(): void {
    this.handleOAuthReturn();
    void this.load();
  }

  meta(provider: string) {
    return PROVIDER_META[provider] ?? { letter: provider[0]?.toUpperCase() ?? '?', blurb: 'Connect this service to Orvanta.' };
  }

  panel(provider: string): AddressPanel {
    return this.panels()[provider] ?? { open: false, loading: false, error: '', source: '', items: [] };
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      const response = await this.api.list();
      this.providers.set(response.statusObject ?? []);
    } catch (error) {
      this.error.set(ApiError.from(error).message);
    } finally {
      this.loading.set(false);
    }
  }

  async connect(provider: ProviderStatus): Promise<void> {
    this.busy.set(provider.provider);
    this.error.set('');
    this.success.set('');
    try {
      const response = await this.api.connect(provider.provider);
      const url = response.statusObject?.authorizationUrl;
      if (!url) {
        throw new ApiError(500, 'No authorization link was returned.');
      }
      window.location.assign(url);
    } catch (error) {
      this.error.set(ApiError.from(error).message);
      this.busy.set(null);
    }
  }

  async disconnect(provider: ProviderStatus): Promise<void> {
    this.busy.set(provider.provider);
    this.error.set('');
    try {
      await this.api.disconnect(provider.provider);
      this.success.set(`${provider.displayName} disconnected.`);
      this.patchPanel(provider.provider, { open: false, items: [], source: '' });
      await this.load();
    } catch (error) {
      this.error.set(ApiError.from(error).message);
    } finally {
      this.busy.set(null);
    }
  }

  toggleAddresses(provider: ProviderStatus): void {
    const open = !this.panel(provider.provider).open;
    this.patchPanel(provider.provider, { open });
    if (open && !this.panel(provider.provider).items.length) {
      void this.loadAddresses(provider.provider, false);
    }
  }

  async loadAddresses(provider: string, refresh: boolean): Promise<void> {
    this.patchPanel(provider, { loading: true, error: '' });
    try {
      const response = await this.api.addresses(provider, refresh);
      this.patchPanel(provider, {
        items: response.statusObject?.addresses ?? [],
        source: response.statusObject?.source ?? ''
      });
    } catch (error) {
      this.patchPanel(provider, { error: ApiError.from(error).message });
    } finally {
      this.patchPanel(provider, { loading: false });
    }
  }

  async makeDefault(provider: string, address: ProviderAddress): Promise<void> {
    try {
      await this.api.setDefaultAddress(provider, address.id);
      this.patchPanel(provider, {
        items: this.panel(provider).items.map(a => ({ ...a, isDefault: a.id === address.id }))
      });
    } catch (error) {
      this.patchPanel(provider, { error: ApiError.from(error).message });
    }
  }

  private patchPanel(provider: string, patch: Partial<AddressPanel>): void {
    this.panels.update(all => ({ ...all, [provider]: { ...this.panel(provider), ...patch } }));
  }

  /** The backend redirects here after the provider's consent screen. */
  private handleOAuthReturn(): void {
    const params = this.route.snapshot.queryParamMap;
    const provider = params.get('provider');
    const status = params.get('status');
    if (!provider || !status) {
      return;
    }

    const name = provider.charAt(0).toUpperCase() + provider.slice(1);
    if (status === 'connected') {
      this.success.set(`${name} connected. You can now order from chat.`);
    } else {
      this.error.set(params.get('message') ?? `${name} authorization was unsuccessful. Please try again.`);
    }

    void this.router.navigate([], { queryParams: {}, replaceUrl: true });
  }
}
