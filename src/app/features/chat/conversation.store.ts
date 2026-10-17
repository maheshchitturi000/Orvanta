import { Injectable, inject, signal } from '@angular/core';
import { Conversation, ConversationService } from './conversation.service';

/**
 * Shared conversation state for the sidebar (history list) and the chat page.
 * Signals keep both in sync without either component knowing about the other.
 */
@Injectable({ providedIn: 'root' })
export class ConversationStore {
  private readonly api = inject(ConversationService);

  private readonly _conversations = signal<Conversation[]>([]);
  private readonly _loading = signal(false);
  private readonly _error = signal('');
  private readonly _activeId = signal<string | null>(null);
  private readonly _resetToken = signal(0);

  readonly conversations = this._conversations.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();
  readonly activeId = this._activeId.asReadonly();

  /** Increments whenever the user asks for a fresh conversation. */
  readonly resetToken = this._resetToken.asReadonly();

  async refresh(): Promise<void> {
    this._loading.set(true);
    this._error.set('');
    try {
      this._conversations.set(await this.api.list());
    } catch {
      this._error.set('Recent conversations are unavailable.');
    } finally {
      this._loading.set(false);
    }
  }

  setActive(conversationId: string | null): void {
    this._activeId.set(conversationId);
  }

  startNew(): void {
    this._activeId.set(null);
    this._resetToken.update(n => n + 1);
  }
}
