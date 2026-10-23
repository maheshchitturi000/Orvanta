import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { ApiError } from '../../../core/models/response-cls.model';
import { Icon } from '../../../shared/ui/icon/icon';
import { IntegrationLogo } from '../integration-logo/integration-logo';
import { IntegrationsStore } from '../integrations.store';
import { ONDC_CITIES, OndcCatalogItem, OndcNetworkStatus, OndcOrderView, OndcService } from './ondc.service';

/** Order states in which the order is finished with the buyer's part (placed or failed). */
const PLACED_STATES = new Set(['Confirmed', 'Accepted', 'In-progress', 'Out-for-delivery', 'Completed']);

/**
 * /integrations/food/ondc — Orvanta as an ONDC buyer app: search restaurants on the network, get a seller's
 * quote, place a cash-on-delivery order and follow it.
 */
@Component({
  selector: 'app-ondc-detail',
  imports: [CurrencyPipe, DatePipe, ReactiveFormsModule, RouterLink, Icon, IntegrationLogo],
  templateUrl: './ondc-detail.html',
  styleUrls: ['../integration-detail.scss', './ondc-detail.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class OndcDetail {
  private readonly api = inject(OndcService);
  private readonly auth = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  protected readonly store = inject(IntegrationsStore);

  protected readonly cities = ONDC_CITIES;
  protected readonly quantities = [1, 2, 3, 4, 5];
  protected readonly item = computed(() => this.store.find('ondc'));

  protected readonly network = signal<OndcNetworkStatus | null>(null);
  protected readonly networkError = signal('');

  protected readonly query = signal('');
  protected readonly city = signal<string>('std:080');
  protected readonly searching = signal(false);
  protected readonly searchError = signal('');
  protected readonly searchNote = signal('');
  protected readonly results = signal<OndcCatalogItem[] | null>(null);
  protected readonly quantity = signal<Record<number, number>>({});

  /** The order being checked out (quote → delivery → placed). */
  protected readonly current = signal<OndcOrderView | null>(null);
  protected readonly quotingId = signal<number | null>(null);
  protected readonly placing = signal(false);
  protected readonly refreshing = signal(false);
  protected readonly orderError = signal('');
  protected readonly orderNote = signal('');

  protected readonly orders = signal<OndcOrderView[]>([]);

  protected readonly isPlaced = computed(() => PLACED_STATES.has(this.current()?.order.state ?? ''));
  protected readonly canPlace = computed(() => this.current()?.order.state === 'Quoted');

  protected readonly delivery = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    phone: ['', [Validators.required, Validators.pattern(/^[6-9]\d{9}$/)]],
    addressLine: ['', [Validators.required, Validators.minLength(5), Validators.maxLength(300)]],
    city: ['', [Validators.required, Validators.maxLength(60)]],
    areaCode: ['', [Validators.required, Validators.pattern(/^\d{6}$/)]]
  });

  constructor() {
    void this.store.load();
    void this.loadNetwork();
    void this.loadOrders();

    const user = this.auth.user();
    if (user) {
      this.delivery.patchValue({ name: user.fullName, phone: user.phoneNumber ?? '' });
    }
  }

  protected cityName(code: string): string {
    return this.cities.find(c => c.code === code)?.name ?? code;
  }

  protected qty(item: OndcCatalogItem): number {
    return this.quantity()[item.catalogItemId] ?? 1;
  }

  protected setQty(item: OndcCatalogItem, value: string): void {
    this.quantity.update(q => ({ ...q, [item.catalogItemId]: Number(value) }));
  }

  protected showError(control: keyof typeof this.delivery.controls): boolean {
    const c = this.delivery.controls[control];
    return c.invalid && c.touched;
  }

  protected async search(event?: Event): Promise<void> {
    event?.preventDefault();
    const query = this.query().trim();
    if (query.length < 2 || this.searching()) {
      return;
    }
    this.searching.set(true);
    this.searchError.set('');
    this.searchNote.set('');
    try {
      const response = await this.api.search(query, this.city());
      this.results.set(response.statusObject?.items ?? []);
      this.searchNote.set(response.statusObject?.items.length ? '' : response.statusMessage);
    } catch (error) {
      this.results.set(null);
      this.searchError.set(ApiError.from(error).message);
    } finally {
      this.searching.set(false);
    }
  }

  protected async getQuote(item: OndcCatalogItem): Promise<void> {
    this.quotingId.set(item.catalogItemId);
    this.orderError.set('');
    this.orderNote.set('');
    try {
      const response = await this.api.quote(item.catalogItemId, this.qty(item));
      this.current.set(response.statusObject);
      if (response.statusObject?.order.state !== 'Quoted') {
        this.orderNote.set(response.statusMessage);
      }
    } catch (error) {
      this.showOrderError(error);
    } finally {
      this.quotingId.set(null);
    }
  }

  protected async place(): Promise<void> {
    const order = this.current()?.order;
    if (!order) return;
    if (this.delivery.invalid) {
      this.delivery.markAllAsTouched();
      return;
    }
    this.placing.set(true);
    this.orderError.set('');
    this.orderNote.set('');
    try {
      const response = await this.api.place(order.orderId, this.delivery.getRawValue());
      this.current.set(response.statusObject);
      if (!PLACED_STATES.has(response.statusObject?.order.state ?? '')) {
        this.orderNote.set(response.statusMessage);
      }
      await this.loadOrders();
    } catch (error) {
      this.showOrderError(error);
    } finally {
      this.placing.set(false);
    }
  }

  protected async refresh(view: OndcOrderView): Promise<void> {
    this.refreshing.set(true);
    this.orderError.set('');
    try {
      const response = await this.api.refresh(view.order.orderId);
      if (response.statusObject) {
        const latest = response.statusObject;
        if (this.current()?.order.orderId === latest.order.orderId) this.current.set(latest);
        this.orders.update(all => all.map(o => (o.order.orderId === latest.order.orderId ? latest : o)));
      }
    } catch (error) {
      this.orderError.set(ApiError.from(error).message);
    } finally {
      this.refreshing.set(false);
    }
  }

  protected open(view: OndcOrderView): void {
    this.current.set(view);
    this.orderError.set('');
    this.orderNote.set('');
  }

  protected closeOrder(): void {
    this.current.set(null);
    this.orderError.set('');
    this.orderNote.set('');
  }

  private showOrderError(error: unknown): void {
    const apiError = ApiError.from(error);
    // A declined quote or order comes back with the order itself, so the page can show its state.
    const view = apiError.statusObject as OndcOrderView | null;
    if (view?.order) {
      this.current.set(view);
    }
    this.orderError.set(apiError.message);
  }

  private async loadNetwork(): Promise<void> {
    try {
      const response = await this.api.status();
      this.network.set(response.statusObject);
      if (response.statusObject?.city) this.city.set(response.statusObject.city);
    } catch (error) {
      this.networkError.set(ApiError.from(error).message);
    }
  }

  private async loadOrders(): Promise<void> {
    try {
      this.orders.set((await this.api.orders()).statusObject ?? []);
    } catch {
      // The list is secondary; the page works without it.
    }
  }
}
