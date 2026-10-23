import { Conversation } from '../../../features/chat/conversation.service';
import { groupConversations, parseUtc } from './history-groups';

function conversation(id: string, updatedAt: Date): Conversation {
  // The API sends UTC without a zone suffix.
  const iso = updatedAt.toISOString().replace('Z', '');
  return { conversationId: id, title: id, createdAt: iso, updatedAt: iso };
}

describe('groupConversations', () => {
  const now = new Date(2026, 9, 18, 18, 30); // 18 Oct 2026, local time
  const at = (daysAgo: number, hour = 12) => new Date(2026, 9, 18 - daysAgo, hour, 0);

  it('buckets by local day relative to now, newest group first', () => {
    const groups = groupConversations(
      [
        conversation('today', at(0, 0)),
        conversation('yesterday', at(1, 23)),
        conversation('week', at(5)),
        conversation('month', at(20)),
        conversation('august', new Date(2026, 7, 3))
      ],
      now
    );

    expect(groups.map(g => [g.key, g.items.map(c => c.conversationId)])).toEqual([
      ['today', ['today']],
      ['yesterday', ['yesterday']],
      ['week', ['week']],
      ['month', ['month']],
      ['2026-7', ['august']]
    ]);
    expect(groups[4].label).toContain('August');
  });

  it('includes the year for months of earlier years', () => {
    const [group] = groupConversations([conversation('old', new Date(2025, 11, 2))], now);
    expect(group.label).toContain('2025');
  });

  it('returns no groups for no conversations', () => {
    expect(groupConversations([], now)).toEqual([]);
  });
});

describe('parseUtc', () => {
  it('reads zone-less timestamps as UTC and keeps explicit zones', () => {
    expect(parseUtc('2026-10-18T06:55:24').toISOString()).toBe('2026-10-18T06:55:24.000Z');
    expect(parseUtc('2026-10-18T06:55:24Z').toISOString()).toBe('2026-10-18T06:55:24.000Z');
    expect(parseUtc('2026-10-18T12:25:24+05:30').toISOString()).toBe('2026-10-18T06:55:24.000Z');
  });
});
