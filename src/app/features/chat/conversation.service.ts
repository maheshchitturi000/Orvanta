import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../core/config/api.config';

export interface Conversation {
  conversationId: string;
  title: string;
  createdAt: string;
  updatedAt: string;
}

export interface SavedMessage {
  messageId: number;
  conversationId: string;
  role: 'user' | 'assistant' | 'system';
  content: string;
  createdAt: string;
}

/**
 * Client for the existing ConversationService (chat history + Ollama replies).
 * That service predates ResponseCls, so it is called with plain HttpClient.
 * Requests still carry the user's JWT via the auth interceptor.
 */
@Injectable({ providedIn: 'root' })
export class ConversationService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  list(): Promise<Conversation[]> {
    return firstValueFrom(this.http.get<Conversation[]>(`${this.baseUrl}/conversations`));
  }

  create(title: string): Promise<{ conversationId: string; title: string }> {
    return firstValueFrom(this.http.post<{ conversationId: string; title: string }>(`${this.baseUrl}/conversations`, { title }));
  }

  messages(conversationId: string): Promise<SavedMessage[]> {
    return firstValueFrom(this.http.get<SavedMessage[]>(`${this.baseUrl}/conversations/${conversationId}/messages`));
  }

  saveMessage(conversationId: string, role: 'user' | 'assistant', content: string): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.baseUrl}/conversations/${conversationId}/messages`, { role, content }));
  }

  async chat(message: string): Promise<string> {
    const response = await firstValueFrom(this.http.post<{ reply: string }>(`${this.baseUrl}/chat`, { message }));
    return response.reply;
  }
}
