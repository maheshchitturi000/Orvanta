import { Injectable, inject } from '@angular/core';
import { ApiService } from '../../core/http/api.service';
import { ResponseCls } from '../../core/models/response-cls.model';
import { FoodAssistResult, FoodItem, OrderConfirmation, OrderPreview } from './food.models';

@Injectable({ providedIn: 'root' })
export class FoodService {
  private readonly api = inject(ApiService);

  /** Detects a food request in a chat prompt and returns the ranked top picks. */
  assist(prompt: string): Promise<ResponseCls<FoodAssistResult>> {
    return this.api.post<FoodAssistResult>('/food/assist', { prompt });
  }

  /** Builds the provider cart and returns the bill. Nothing is ordered yet. */
  preview(item: FoodItem, quantity: number): Promise<ResponseCls<OrderPreview>> {
    return this.api.post<OrderPreview>('/food/orders/preview', {
      provider: item.provider,
      restaurantId: item.restaurantId,
      restaurantName: item.restaurantName,
      menuItemId: item.menuItemId,
      itemName: item.name,
      quantity,
      requiresCustomization: item.requiresCustomization
    });
  }

  /** Places the previewed cart as cash on delivery. */
  confirm(previewId: string, noteToRestaurant?: string): Promise<ResponseCls<OrderConfirmation>> {
    return this.api.post<OrderConfirmation>('/food/orders/confirm', { previewId, noteToRestaurant });
  }
}
