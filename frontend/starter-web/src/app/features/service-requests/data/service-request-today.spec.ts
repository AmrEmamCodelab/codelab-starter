import { describe, expect, it } from 'vitest';
import { SERVICE_REQUEST_TIME_ZONE, todayInTimeZone } from './service-request-today';

describe('todayInTimeZone', () => {
  it('gives the calendar date in the app time zone, not in UTC', () => {
    // 2026-11-05T22:30Z is already 2026-11-06 01:30 in Riyadh (UTC+3).
    const lateEveningUtc = Date.UTC(2026, 10, 5, 22, 30);

    expect(todayInTimeZone(lateEveningUtc)).toBe('2026-11-06');
    expect(todayInTimeZone(lateEveningUtc, 'UTC')).toBe('2026-11-05');
  });

  it('pads month and day to two digits', () => {
    expect(todayInTimeZone(Date.UTC(2026, 0, 2, 9), SERVICE_REQUEST_TIME_ZONE)).toBe('2026-01-02');
  });
});
