import { describe, expect, it } from 'vitest';
import { AppLanguages } from '../../../core/i18n/language';
import { localizedName } from './data/lookup.models';
import { filterLookupItems, filterLookupTypes, matchesSearch, pageOf } from './lookup-list';
import { lookupItem, lookupType } from './lookups.testing';

describe('lookup list rules', () => {
  it('matches every word of the search, case-insensitively, across the fields', () => {
    expect(matchesSearch(['GENERAL', 'General enquiry'], '')).toBe(true);
    expect(matchesSearch(['GENERAL', 'General enquiry'], '  enquiry general ')).toBe(true);
    expect(matchesSearch(['GENERAL', 'General enquiry'], 'general account')).toBe(false);
  });

  it('filters types by key or name', () => {
    const types = [lookupType(), lookupType({ type: 'request-categories', nameEn: 'Request categories', nameAr: 'فئات' })];
    expect(filterLookupTypes(types, 'categor').map((type) => type.type)).toEqual(['request-categories']);
    expect(filterLookupTypes(types, 'فئات')).toHaveLength(1);
    expect(filterLookupTypes(types, '')).toHaveLength(2);
  });

  it('orders values by sort order, then code, after filtering', () => {
    const items = [
      lookupItem({ id: 3, code: 'C', sortOrder: 2 }),
      lookupItem({ id: 2, code: 'B', sortOrder: 1 }),
      lookupItem({ id: 1, code: 'A', sortOrder: 2, nameEn: 'Other' }),
    ];
    expect(filterLookupItems(items, '').map((item) => item.code)).toEqual(['B', 'A', 'C']);
    expect(filterLookupItems(items, 'other').map((item) => item.code)).toEqual(['A']);
    expect(items.map((item) => item.code)).toEqual(['C', 'B', 'A']);
  });

  it('pages rows and clamps a page past the end', () => {
    const rows = [1, 2, 3, 4, 5];
    expect(pageOf(rows, 1, 2)).toEqual([1, 2]);
    expect(pageOf(rows, 3, 2)).toEqual([5]);
    expect(pageOf(rows, 9, 2)).toEqual([5]);
    expect(pageOf([], 1, 2)).toEqual([]);
  });

  it('names a type or value in the current language', () => {
    const type = lookupType();
    expect(localizedName(type, AppLanguages.English)).toBe(type.nameEn);
    expect(localizedName(type, AppLanguages.Arabic)).toBe(type.nameAr);
  });
});
