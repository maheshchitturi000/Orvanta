import { TestBed } from '@angular/core/testing';
import { Conversation, ConversationService } from './conversation.service';
import { ConversationStore } from './conversation.store';

describe('ConversationStore', () => {
  const sample: Conversation[] = [
    { conversationId: 'a', title: 'Order biryani', createdAt: '', updatedAt: '' }
  ];

  function setup(list: () => Promise<Conversation[]>) {
    TestBed.configureTestingModule({
      providers: [{ provide: ConversationService, useValue: { list } }]
    });
    return TestBed.inject(ConversationStore);
  }

  it('loads conversations and clears loading', async () => {
    const store = setup(() => Promise.resolve(sample));
    await store.refresh();

    expect(store.conversations()).toEqual(sample);
    expect(store.loading()).toBe(false);
    expect(store.error()).toBe('');
  });

  it('keeps the UI usable when history is unavailable', async () => {
    const store = setup(() => Promise.reject(new Error('down')));
    await store.refresh();

    expect(store.conversations()).toEqual([]);
    expect(store.error()).toContain('unavailable');
  });

  it('startNew clears the active conversation and signals a reset', () => {
    const store = setup(() => Promise.resolve([]));
    store.setActive('a');
    const before = store.resetToken();

    store.startNew();

    expect(store.activeId()).toBeNull();
    expect(store.resetToken()).toBe(before + 1);
  });
});
