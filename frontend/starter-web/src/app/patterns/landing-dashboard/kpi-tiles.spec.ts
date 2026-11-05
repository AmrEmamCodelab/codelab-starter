import { describe, expect, it } from 'vitest';
import { formatKpiValue, hasKpiValue, uniqueTiles } from './kpi-tiles';
import { LANDING_DASHBOARD_TRANSLATION_KEYS } from './landing-dashboard-translation-keys';

describe('hasKpiValue', () => {
  it('accepts finite numbers (zero too) and non-blank strings', () => {
    expect(hasKpiValue(0)).toBe(true);
    expect(hasKpiValue(1250)).toBe(true);
    expect(hasKpiValue('98%')).toBe(true);
    expect(hasKpiValue(null)).toBe(false);
    expect(hasKpiValue('  ')).toBe(false);
    expect(hasKpiValue(Number.NaN)).toBe(false);
  });
});

describe('formatKpiValue', () => {
  it('groups numbers for the language and keeps strings as given', () => {
    expect(formatKpiValue(1250, 'en')).toBe('1,250');
    expect(formatKpiValue(1250, 'ar')).toBe(new Intl.NumberFormat('ar').format(1250));
    expect(formatKpiValue('98%', 'ar')).toBe('98%');
    expect(formatKpiValue(null, 'en')).toBe('');
  });
});

describe('uniqueTiles', () => {
  it('keeps the first tile per label key', () => {
    const tiles = uniqueTiles([
      { labelKey: 'home.open', value: 1 },
      { labelKey: 'home.closed', value: 2 },
      { labelKey: 'home.open', value: 3 },
    ]);
    expect(tiles.map((tile) => tile.value)).toEqual([1, 2]);
  });
});

describe('LandingDashboardTranslationKeys', () => {
  it('has unique keys under patterns.landing-dashboard.*', () => {
    expect(new Set(LANDING_DASHBOARD_TRANSLATION_KEYS).size).toBe(LANDING_DASHBOARD_TRANSLATION_KEYS.length);
    for (const key of LANDING_DASHBOARD_TRANSLATION_KEYS) {
      expect(key).toMatch(/^patterns\.landing-dashboard\.[a-z-]+$/);
    }
  });
});
