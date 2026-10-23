import { Injectable, inject } from '@angular/core';
import { ApiService } from '../../core/http/api.service';

/** Row of GET /api/auth/activity/summary: actions on one local day in one area. */
export interface ActivitySummaryRow {
  day: string; // yyyy-MM-dd, in the user's local time
  area: string; // Auth, Food, Integrations, Jobs...
  total: number;
  failed: number;
}

export interface DayBucket {
  date: Date;
  key: string;
  total: number;
  failed: number;
  byArea: { label: string; total: number }[];
}

export interface AreaBucket {
  area: string;
  label: string;
  total: number;
}

/** Audit areas as people call them. */
const AREA_LABELS: Record<string, string> = {
  Auth: 'Account',
  Integrations: 'Connected apps',
  Food: 'Food',
  Ondc: 'ONDC',
  Jobs: 'Jobs'
};

export const areaLabel = (area: string) => AREA_LABELS[area] ?? area;

@Injectable({ providedIn: 'root' })
export class ActivityService {
  private readonly api = inject(ApiService);

  /** The user's actions for the last `days` days, split at their local midnight. */
  async summary(days = 14): Promise<ActivitySummaryRow[]> {
    const utcOffsetMinutes = -new Date().getTimezoneOffset();
    const response = await this.api.get<ActivitySummaryRow[]>('/auth/activity/summary', { days, utcOffsetMinutes });
    return response.statusObject ?? [];
  }
}

/** One bucket per day (including empty days), oldest first, ending today. */
export function toDays(rows: ActivitySummaryRow[], days: number, today = new Date()): DayBucket[] {
  const buckets: DayBucket[] = [];
  for (let i = days - 1; i >= 0; i--) {
    const date = new Date(today.getFullYear(), today.getMonth(), today.getDate() - i);
    const key = localKey(date);
    const forDay = rows.filter(r => r.day.slice(0, 10) === key);
    buckets.push({
      date,
      key,
      total: forDay.reduce((sum, r) => sum + r.total, 0),
      failed: forDay.reduce((sum, r) => sum + r.failed, 0),
      byArea: forDay.map(r => ({ label: areaLabel(r.area), total: r.total })).sort((a, b) => b.total - a.total)
    });
  }
  return buckets;
}

/** Totals per area, largest first. */
export function toAreas(rows: ActivitySummaryRow[]): AreaBucket[] {
  const totals = new Map<string, number>();
  for (const r of rows) {
    totals.set(r.area, (totals.get(r.area) ?? 0) + r.total);
  }
  return [...totals.entries()]
    .map(([area, total]) => ({ area, label: areaLabel(area), total }))
    .sort((a, b) => b.total - a.total);
}

/** Axis maximum rounded up to 1, 2 or 5 × 10^n so ticks are clean numbers. */
export function niceMax(value: number): number {
  if (value <= 1) {
    return 1;
  }
  const magnitude = 10 ** Math.floor(Math.log10(value));
  const step = [1, 2, 5, 10].find(m => m * magnitude >= value)!;
  return step * magnitude;
}

function localKey(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}
