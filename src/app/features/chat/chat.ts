import { Component, ElementRef, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { ApiError } from '../../core/models/response-cls.model';
import { FoodResults } from '../food/food-results/food-results';
import { FoodAssistResult, OrderConfirmation } from '../food/food.models';
import { FoodService } from '../food/food.service';
import { ConversationService, Conversation } from './conversation.service';

interface ChatMessage {
  role: 'user' | 'assistant';
  content: string;
  food?: FoodAssistResult;
}

const WELCOME: ChatMessage = {
  role: 'assistant',
  content: 'Hello! I am Orvanta AI. Ask me anything, or tell me what you feel like eating and I’ll find the top 5 dishes on Swiggy and Zomato.'
};

@Component({
  selector: 'app-chat',
  imports: [FormsModule, RouterLink, FoodResults],
  templateUrl: './chat.html',
  styleUrl: './chat.scss'
})
export class Chat implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly conversationsApi = inject(ConversationService);
  private readonly food = inject(FoodService);
  private readonly router = inject(Router);
  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');

  readonly user = this.auth.user;
  readonly firstName = this.auth.firstName;
  readonly initials = computed(() =>
    (this.user()?.fullName ?? '?')
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map(p => p[0]!.toUpperCase())
      .join('')
  );

  readonly messages = signal<ChatMessage[]>([WELCOME]);
  readonly conversations = signal<Conversation[]>([]);
  readonly conversationId = signal<string | null>(null);
  readonly input = signal('');
  readonly loading = signal(false);
  readonly historyLoading = signal(false);
  readonly historyError = signal('');
  readonly error = signal('');
  readonly sidebarOpen = signal(false);

  readonly suggestions = [
    'Order chicken biryani',
    'Veg pizza from Swiggy',
    'Order the top one masala dosa',
    'Plan my weekend in Goa'
  ];

  ngOnInit(): void {
    void this.loadConversations();
  }

  onMessageKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      void this.sendMessage();
    }
  }

  useSuggestion(text: string): void {
    this.input.set(text);
    void this.sendMessage();
  }

  async loadConversations(): Promise<void> {
    this.historyLoading.set(true);
    this.historyError.set('');
    try {
      this.conversations.set(await this.conversationsApi.list());
    } catch {
      this.historyError.set('Recent conversations are unavailable.');
    } finally {
      this.historyLoading.set(false);
    }
  }

  newConversation(): void {
    if (this.loading()) return;
    this.conversationId.set(null);
    this.input.set('');
    this.error.set('');
    this.messages.set([WELCOME]);
    this.sidebarOpen.set(false);
  }

  async openConversation(conversation: Conversation): Promise<void> {
    if (this.loading()) return;

    this.conversationId.set(conversation.conversationId);
    this.loading.set(true);
    this.error.set('');
    this.sidebarOpen.set(false);
    try {
      const saved = await this.conversationsApi.messages(conversation.conversationId);
      this.messages.set(
        saved
          .filter(m => m.role === 'user' || m.role === 'assistant')
          .map(m => ({ role: m.role as 'user' | 'assistant', content: m.content }))
      );
      this.scrollToBottom();
    } catch {
      this.error.set('Could not open this conversation.');
    } finally {
      this.loading.set(false);
    }
  }

  async sendMessage(): Promise<void> {
    const message = this.input().trim();
    if (!message || this.loading()) return;

    this.input.set('');
    this.loading.set(true);
    this.error.set('');
    this.messages.update(list => [...list, { role: 'user', content: message }]);
    this.scrollToBottom();

    const conversationId = await this.ensureConversation(message);
    void this.persist(conversationId, 'user', message);

    try {
      // Food requests are handled by the FoodService; everything else goes to the AI model.
      const food = await this.tryFood(message);
      if (food) {
        const summary = this.summarise(food.statusMessage, food.result);
        this.messages.update(list => [...list, { role: 'assistant', content: food.statusMessage, food: food.result }]);
        void this.persist(conversationId, 'assistant', summary);
        return;
      }

      const reply = await this.conversationsApi.chat(message);
      this.messages.update(list => [...list, { role: 'assistant', content: reply }]);
      void this.persist(conversationId, 'assistant', reply);
    } catch (error) {
      this.error.set(ApiError.from(error).message);
    } finally {
      this.loading.set(false);
      this.scrollToBottom();
      void this.loadConversations();
    }
  }

  onOrdered(order: OrderConfirmation): void {
    const text = `Order placed: ${order.itemName} × ${order.quantity} from ${order.restaurantName} (${order.provider}). Pay ₹${order.totalAmount} cash on delivery.`;
    void this.persist(this.conversationId(), 'assistant', text);
  }

  async signOut(): Promise<void> {
    await this.auth.logout();
  }

  openIntegrations(): void {
    void this.router.navigate(['/integrations']);
  }

  private async tryFood(message: string): Promise<{ statusMessage: string; result: FoodAssistResult } | null> {
    try {
      const response = await this.food.assist(message);
      const result = response.statusObject;
      return result?.isFoodIntent ? { statusMessage: response.statusMessage, result } : null;
    } catch (error) {
      // If the food service is down, fall back to normal chat rather than failing the message.
      console.warn('Food assist unavailable', error);
      return null;
    }
  }

  private summarise(heading: string, result: FoodAssistResult): string {
    const lines = result.items.map(r => `${r.rank}. ${r.item.name} – ${r.item.restaurantName} (${r.item.providerName})${r.item.price !== null ? ` ₹${r.item.price}` : ''}`);
    return [heading, ...lines].join('\n');
  }

  private async ensureConversation(firstMessage: string): Promise<string | null> {
    if (this.conversationId()) return this.conversationId();
    try {
      const created = await this.conversationsApi.create(firstMessage.substring(0, 200));
      this.conversationId.set(created.conversationId);
      return created.conversationId;
    } catch {
      return null; // History is optional; chatting still works.
    }
  }

  private async persist(conversationId: string | null, role: 'user' | 'assistant', content: string): Promise<void> {
    if (!conversationId) return;
    try {
      await this.conversationsApi.saveMessage(conversationId, role, content);
    } catch (error) {
      console.warn('Could not save message', error);
    }
  }

  private scrollToBottom(): void {
    setTimeout(() => {
      const el = this.scroller()?.nativeElement;
      el?.scrollTo({ top: el.scrollHeight, behavior: 'smooth' });
    });
  }
}
