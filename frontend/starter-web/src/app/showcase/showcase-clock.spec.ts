import { describe, expect, it } from 'vitest';
import { SHOWCASE_NOW_ISO, SHOWCASE_NOW_MS, formatShowcaseDate, hoursFromNow, showcaseClock, showcaseToday } from './showcase-clock';

describe('the showcase clock', () => {
  it('is pinned to SHOWCASE_NOW_ISO', () => {
    expect(showcaseClock()).toBe(SHOWCASE_NOW_MS);
    expect(new Date(SHOWCASE_NOW_MS).toISOString()).toBe('2026-11-01T06:00:00.000Z');
    expect(SHOWCASE_NOW_ISO).toContain('+03:00');
  });

  it('reads today in Saudi time, also just after midnight there', () => {
    expect(showcaseToday(showcaseClock)).toBe('2026-11-01');
    const justAfterMidnightRiyadh = Date.parse('2026-10-31T21:30:00Z');
    expect(showcaseToday(() => justAfterMidnightRiyadh)).toBe('2026-11-01');
  });

  it('places relative fixtures before and after now', () => {
    expect(hoursFromNow(showcaseClock, 2)).toBe('2026-11-01T08:00:00.000Z');
    expect(hoursFromNow(showcaseClock, -30)).toBe('2026-10-31T00:00:00.000Z');
  });

  it('formats a long Gregorian date per language', () => {
    expect(formatShowcaseDate(SHOWCASE_NOW_MS, 'en')).toBe('November 1, 2026');
    expect(formatShowcaseDate(SHOWCASE_NOW_MS, 'ar')).toContain('نوفمبر');
  });
});
