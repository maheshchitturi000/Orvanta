import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '../../../core/models/response-cls.model';
import { FoodAssistResult, OrderConfirmation, OrderPreview, RankedFoodItem } from '../food.models';
import { FoodService } from '../food.service';

type Step = 'browse' | 'quantity' | 'review' | 'done';

/**
 * Shows the top picks for a chat prompt and walks the user through
 * pick → review bill → confirm cash-on-delivery order.
 */
@Component({
  selector: 'app-food-results',
  imports: [CurrencyPipe, DecimalPipe, FormsModule, RouterLink],
  templateUrl: './food-results.html',
  styleUrl: './food-results.scss'
})
export class FoodResults implements OnInit {
  private readonly food = inject(FoodService);

  readonly result = input.required<FoodAssistResult>();
  readonly ordered = output<OrderConfirmation>();

  readonly step = signal<Step>('browse');
  readonly selected = signal<RankedFoodItem | null>(null);
  readonly quantity = signal(1);
  readonly note = signal('');
  readonly preview = signal<OrderPreview | null>(null);
  readonly confirmation = signal<OrderConfirmation | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');

  ngOnInit(): void {
    const result = this.result();
    // "Order the top one…" pre-selects the best match; the user still reviews the bill.
    if (result.autoPickTop && result.items.length) {
      this.choose(result.items[0]);
    }
  }

  choose(ranked: RankedFoodItem): void {
    this.selected.set(ranked);
    this.quantity.set(1);
    this.preview.set(null);
    this.error.set('');
    this.step.set('quantity');
  }

  changeQuantity(delta: number): void {
    this.quantity.update(q => Math.min(10, Math.max(1, q + delta)));
  }

  back(): void {
    this.error.set('');
    if (this.step() === 'review') {
      this.step.set('quantity');
    } else {
      this.selected.set(null);
      this.step.set('browse');
    }
  }

  async review(): Promise<void> {
    const ranked = this.selected();
    if (!ranked || this.busy()) return;

    this.busy.set(true);
    this.error.set('');
    try {
      const response = await this.food.preview(ranked.item, this.quantity());
      this.preview.set(response.statusObject);
      this.step.set('review');
    } catch (error) {
      this.error.set(ApiError.from(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  async placeOrder(): Promise<void> {
    const preview = this.preview();
    if (!preview || this.busy()) return;

    this.busy.set(true);
    this.error.set('');
    try {
      const response = await this.food.confirm(preview.previewId, this.note().trim() || undefined);
      if (response.statusObject) {
        this.confirmation.set(response.statusObject);
        this.step.set('done');
        this.ordered.emit(response.statusObject);
      }
    } catch (error) {
      const apiError = ApiError.from(error);
      this.error.set(apiError.message);
      if (apiError.statusCode === 410) {
        // Preview expired: rebuild the bill so the user sees current prices.
        this.preview.set(null);
        this.step.set('quantity');
      }
    } finally {
      this.busy.set(false);
    }
  }
}
