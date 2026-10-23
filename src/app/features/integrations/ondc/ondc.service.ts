import { Injectable, inject } from '@angular/core';
import { ApiService } from '../../../core/http/api.service';
import { ResponseCls } from '../../../core/models/response-cls.model';

export interface OndcChecklistItem {
  label: string;
  done: boolean;
}

/** How Orvanta is connected to ONDC (GET /api/ondc/status). */
export interface OndcNetworkStatus {
  enabled: boolean;
  mode: 'Sandbox' | 'Staging' | 'PreProduction' | 'Production';
  subscriberId: string;
  subscriberUrl: string;
  domain: string;
  city: string;
  readyForNetwork: boolean;
  checklist: OndcChecklistItem[];
}

export interface OndcCatalogItem {
  catalogItemId: number;
  transactionId: string;
  providerName: string;
  itemName: string;
  price: number | null;
  currency: string;
  isVeg: boolean | null;
  imageUrl: string | null;
}

export interface OndcSearchResult {
  transactionId: string;
  query: string;
  city: string;
  items: OndcCatalogItem[];
}

export interface OndcOrder {
  orderId: string;
  providerName: string;
  itemName: string;
  quantity: number;
  /** Quoting, Quoted, Initializing, Initialized, Confirming, Confirmed, Failed, or the seller's own state. */
  state: string;
  quoteTotal: number | null;
  currency: string;
  bppOrderId: string | null;
  error: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface OndcQuoteLine {
  title: string;
  type: string;
  amount: number;
}

export interface OndcDelivery {
  name: string;
  phone: string;
  addressLine: string;
  city: string;
  areaCode: string;
}

export interface OndcOrderView {
  order: OndcOrder;
  quoteLines: OndcQuoteLine[];
  delivery: OndcDelivery | null;
}

/** ONDC city codes Orvanta searches in. */
export const ONDC_CITIES = [
  { code: 'std:080', name: 'Bengaluru' },
  { code: 'std:044', name: 'Chennai' },
  { code: 'std:011', name: 'Delhi' },
  { code: 'std:040', name: 'Hyderabad' },
  { code: 'std:033', name: 'Kolkata' },
  { code: 'std:022', name: 'Mumbai' },
  { code: 'std:020', name: 'Pune' }
] as const;

@Injectable({ providedIn: 'root' })
export class OndcService {
  private readonly api = inject(ApiService);

  status(): Promise<ResponseCls<OndcNetworkStatus>> {
    return this.api.get<OndcNetworkStatus>('/ondc/status');
  }

  search(query: string, city: string): Promise<ResponseCls<OndcSearchResult>> {
    return this.api.post<OndcSearchResult>('/ondc/searches', { query, city });
  }

  /** Asks the restaurant for the final price; creates the order. */
  quote(catalogItemId: number, quantity: number): Promise<ResponseCls<OndcOrderView>> {
    return this.api.post<OndcOrderView>('/ondc/orders', { catalogItemId, quantity });
  }

  /** Places a quoted order with cash on delivery. */
  place(orderId: string, delivery: OndcDelivery): Promise<ResponseCls<OndcOrderView>> {
    return this.api.post<OndcOrderView>(`/ondc/orders/${orderId}/place`, delivery);
  }

  refresh(orderId: string): Promise<ResponseCls<OndcOrderView>> {
    return this.api.post<OndcOrderView>(`/ondc/orders/${orderId}/refresh`);
  }

  orders(): Promise<ResponseCls<OndcOrderView[]>> {
    return this.api.get<OndcOrderView[]>('/ondc/orders');
  }
}
