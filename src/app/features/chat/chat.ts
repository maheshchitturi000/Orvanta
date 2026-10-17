import { Location } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../core/auth/auth.service';
import { ApiError } from '../../core/models/response-cls.model';
import { Icon, IconName } from '../../shared/ui/icon/icon';
import { FoodResults } from '../food/food-results/food-results';
import { FoodAssistResult, OrderConfirmation } from '../food/food.models';
import { FoodService } from '../food/food.service';
import { ConversationService } from './conversation.service';
import { ConversationStore } from './conversation.store';

interface ChatMessage {
  role: 'user' | 'assistant';
  content: string;
  food?: FoodAssistResult;
}

interface Suggestion {
  icon: IconName;
  title: string;
  prompt: string;
}

/**
 * Chat page. Route "/" starts a new conversation, "/c/:conversationId" opens one.
 * Food requests go to the FoodService; everything else goes to the AI model.
 */
@Component({
  selector: 'app-chat',
  imports: [FormsModule, FoodResults, Icon],
  templateUrl: './chat.html',
  styleUrl: './chat.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Chat {
  private readonly auth = inject(AuthService);
  private readonly api = inject(ConversationService);
  private readonly store = inject(ConversationStore);
  private readonly food = inject(FoodService);
  private readonly location = inject(Location);
  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');

  /** Bound from the route parameter (withComponentInputBinding). */
  readonly conversationId = input<string | undefined>();

  protected readonly firstName = this.auth.firstName;
  protected readonly initials = computed(() =>
    (this.auth.user()?.fullName ?? '?')
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map(part => part[0]!.toUpperCase())
      .join('')
  );

  protected readonly messages = signal<ChatMessage[]>([]);
  protected readonly input = signal('');
  protected readonly sending = signal(false);
  protected readonly loadingHistory = signal(false);
  protected readonly error = signal('');
  protected readonly isEmpty = computed(() => this.messages().length === 0 && !this.loadingHistory());

  protected readonly suggestions: Suggestion[] = [
    { icon: 'food', title: 'Order chicken biryani', prompt: 'Order chicken biryani' },
    { icon: 'food', title: 'Veg pizza from Swiggy', prompt: 'Veg pizza from Swiggy' },
    { icon: 'sparkles', title: 'Order the top masala dosa', prompt: 'Order the top one masala dosa' },
    { icon: 'message', title: 'Plan a weekend in Goa', prompt: 'Plan my weekend in Goa' }
  ];

  /** Id of the conversation being shown (from the route, or created on first message). */
  private currentId: string | null = null;

  constructor() {
    // Load whenever the routed conversation changes.
    effect(() => {
      const id = this.conversationId() ?? null;
      untracked(() => (id ? void this.open(id) : this.reset()));
    });

    // "New conversation" from the sidebar while already on this page.
    let lastToken = this.store.resetToken();
    effect(() => {
      const token = this.store.resetToken();
      if (token !== lastToken) {
        lastToken = token;
        untracked(() => {
          this.reset();
          this.location.replaceState('/');
        });
      }
    });
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      void this.send();
    }
  }

  protected useSuggestion(suggestion: Suggestion): void {
    this.input.set(suggestion.prompt);
    void this.send();
  }

  protected async send(): Promise<void> {
    const text = this.input().trim();
    if (!text || this.sending()) return;

    this.input.set('');
    this.error.set('');
    this.sending.set(true);
    this.append({ role: 'user', content: text });

    const conversationId = await this.ensureConversation(text);
    void this.persist(conversationId, 'user', text);

    try {
      const food = await this.tryFood(text);
      if (food) {
        this.append({ role: 'assistant', content: food.message, food: food.result });
        void this.persist(conversationId, 'assistant', this.summarise(food.message, food.result));
        return;
      }

      const reply = await this.api.chat(text);
      this.append({ role: 'assistant', content: reply });
      void this.persist(conversationId, 'assistant', reply);
    } catch (error) {
      this.error.set(ApiError.from(error).message);
    } finally {
      this.sending.set(false);
      void this.store.refresh();
    }
  }

  protected onOrdered(order: OrderConfirmation): void {
    const text = `Order placed: ${order.itemName} × ${order.quantity} from ${order.restaurantName} (${order.provider}). Pay ₹${order.totalAmount} cash on delivery.`;
    void this.persist(this.currentId, 'assistant', text);
  }

  private async open(conversationId: string): Promise<void> {
    this.currentId = conversationId;
    this.store.setActive(conversationId);
    this.error.set('');
    this.loadingHistory.set(true);
    this.messages.set([]);

    try {
      const saved = await this.api.messages(conversationId);
      if (this.currentId !== conversationId) return; // user moved on meanwhile
      this.messages.set(
        saved
          .filter(m => m.role === 'user' || m.role === 'assistant')
          .map(m => ({ role: m.role as 'user' | 'assistant', content: m.content }))
      );
      this.scrollToBottom('auto');
    } catch {
      this.error.set('Could not open this conversation.');
    } finally {
      this.loadingHistory.set(false);
    }
  }

  private reset(): void {
    this.currentId = null;
    this.store.setActive(null);
    this.messages.set([]);
    this.input.set('');
    this.error.set('');
  }

  private append(message: ChatMessage): void {
    this.messages.update(list => [...list, message]);
    this.scrollToBottom();
  }

  private async tryFood(text: string): Promise<{ message: string; result: FoodAssistResult } | null> {
    try {
      const response = await this.food.assist(text);
      return response.statusObject?.isFoodIntent ? { message: response.statusMessage, result: response.statusObject } : null;
    } catch (error) {
      // If the food service is unavailable, fall back to the normal AI reply.
      console.warn('Food assist unavailable', error);
      return null;
    }
  }

  private summarise(heading: string, result: FoodAssistResult): string {
    const lines = result.items.map(
      r => `${r.rank}. ${r.item.name} – ${r.item.restaurantName} (${r.item.providerName})${r.item.price !== null ? ` ₹${r.item.price}` : ''}`
    );
    return [heading, ...lines].join('\n');
  }

  /** Creates the conversation on the first message and reflects it in the URL without reloading the page. */
  private async ensureConversation(firstMessage: string): Promise<string | null> {
    if (this.currentId) return this.currentId;
    try {
      const created = await this.api.create(firstMessage.substring(0, 200));
      this.currentId = created.conversationId;
      this.store.setActive(created.conversationId);
      this.location.replaceState(`/c/${created.conversationId}`);
      return created.conversationId;
    } catch {
      return null; // History is optional; chatting still works.
    }
  }

  private async persist(conversationId: string | null, role: 'user' | 'assistant', content: string): Promise<void> {
    if (!conversationId) return;
    try {
      await this.api.saveMessage(conversationId, role, content);
    } catch (error) {
      console.warn('Could not save message', error);
    }
  }

  private scrollToBottom(behavior: ScrollBehavior = 'smooth'): void {
    setTimeout(() => {
      const el = this.scroller()?.nativeElement;
      el?.scrollTo({ top: el.scrollHeight, behavior });
    });
  }
}
