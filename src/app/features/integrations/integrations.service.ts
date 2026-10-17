import { Injectable, inject } from '@angular/core';
import { ApiService } from '../../core/http/api.service';
import { ResponseCls } from '../../core/models/response-cls.model';

export interface ProviderStatus {
  provider: string;
  displayName: string;
  category: string;
  enabled: boolean;
  isConnected: boolean;
  connectedAt: string | null;
}

export interface ProviderAddress {
  id: string;
  label: string | null;
  addressLine: string | null;
  phoneNumber: string | null;
  isDefault: boolean;
}

export interface AddressesResult {
  provider: string;
  source: string;
  addresses: ProviderAddress[];
}

@Injectable({ providedIn: 'root' })
export class IntegrationsService {
  private readonly api = inject(ApiService);

  list(): Promise<ResponseCls<ProviderStatus[]>> {
    return this.api.get<ProviderStatus[]>('/integrations');
  }

  connect(provider: string): Promise<ResponseCls<{ authorizationUrl: string }>> {
    return this.api.post<{ authorizationUrl: string }>(`/integrations/${provider}/connect`);
  }

  disconnect(provider: string): Promise<ResponseCls<unknown>> {
    return this.api.delete(`/integrations/${provider}`);
  }

  addresses(provider: string, refresh = false): Promise<ResponseCls<AddressesResult>> {
    return this.api.get<AddressesResult>(`/integrations/${provider}/addresses`, refresh ? { refresh: true } : undefined);
  }

  setDefaultAddress(provider: string, addressId: string): Promise<ResponseCls<unknown>> {
    return this.api.put(`/integrations/${provider}/addresses/default`, { addressId });
  }
}
