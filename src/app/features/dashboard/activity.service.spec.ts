import { ActivitySummaryRow, niceMax, toAreas, toDays } from './activity.service';

const rows: ActivitySummaryRow[] = [
  { day: '2026-10-18', area: 'Auth', total: 5, failed: 2 },
  { day: '2026-10-18', area: 'Integrations', total: 1, failed: 0 },
  { day: '2026-10-16', area: 'Food', total: 3, failed: 0 }
];

describe('toDays', () => {
  const today = new Date(2026, 9, 18, 21, 30);

  it('returns one bucket per day ending today, including empty days', () => {
    const days = toDays(rows, 4, today);
    expect(days.map(d => [d.key, d.total])).toEqual([
      ['2026-10-15', 0],
      ['2026-10-16', 3],
      ['2026-10-17', 0],
      ['2026-10-18', 6]
    ]);
  });

  it('keeps failures and a per-area breakdown, largest first', () => {
    const last = toDays(rows, 1, today)[0];
    expect(last.failed).toBe(2);
    expect(last.byArea).toEqual([
      { label: 'Account', total: 5 },
      { label: 'Connected apps', total: 1 }
    ]);
  });
});

describe('toAreas', () => {
  it('sums each area with readable labels, largest first', () => {
    expect(toAreas(rows)).toEqual([
      { area: 'Auth', label: 'Account', total: 5 },
      { area: 'Food', label: 'Food', total: 3 },
      { area: 'Integrations', label: 'Connected apps', total: 1 }
    ]);
  });
});

describe('niceMax', () => {
  it('rounds the axis maximum up to 1, 2 or 5 × 10^n', () => {
    expect([0, 1, 3, 6, 15, 42, 180].map(niceMax)).toEqual([1, 1, 5, 10, 20, 50, 200]);
  });
});
