import { Conversation } from '../../../features/chat/conversation.service';

export interface HistoryGroup {
  key: string;
  label: string;
  items: Conversation[];
}

const DAY_MS = 24 * 60 * 60 * 1000;

/** The API sends UTC timestamps without a zone ("2026-10-18T06:55:24"); read them as UTC. */
export function parseUtc(value: string): Date {
  return new Date(/[zZ]|[+-]\d{2}:?\d{2}$/.test(value) ? value : `${value}Z`);
}

/**
 * Buckets conversations (already newest first) into Today / Yesterday / Previous 7 days /
 * Previous 30 days / one group per older month, in the user's local time.
 */
export function groupConversations(conversations: Conversation[], now = new Date()): HistoryGroup[] {
  const startOfToday = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
  const groups = new Map<string, HistoryGroup>();

  for (const conversation of conversations) {
    const when = parseUtc(conversation.updatedAt || conversation.createdAt);
    const { key, label } = bucket(when, startOfToday);
    if (!groups.has(key)) {
      groups.set(key, { key, label, items: [] });
    }
    groups.get(key)!.items.push(conversation);
  }

  return [...groups.values()];
}

function bucket(when: Date, startOfToday: number): { key: string; label: string } {
  const time = when.getTime();
  if (Number.isNaN(time) || time >= startOfToday) return { key: 'today', label: 'Today' };
  if (time >= startOfToday - DAY_MS) return { key: 'yesterday', label: 'Yesterday' };
  if (time >= startOfToday - 7 * DAY_MS) return { key: 'week', label: 'Previous 7 days' };
  if (time >= startOfToday - 30 * DAY_MS) return { key: 'month', label: 'Previous 30 days' };

  const sameYear = when.getFullYear() === new Date(startOfToday).getFullYear();
  return {
    key: `${when.getFullYear()}-${when.getMonth()}`,
    label: when.toLocaleDateString(undefined, sameYear ? { month: 'long' } : { month: 'long', year: 'numeric' })
  };
}
