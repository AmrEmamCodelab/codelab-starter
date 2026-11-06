import { clampPage } from '../../../patterns';
import type { AdminLookupItem, LookupType } from './data/lookup.models';

/**
 * The client-side list rules of the lookups screens. The endpoints answer whole lists (a type holds tens of values,
 * not thousands), so search, order and paging happen here.
 */

/** True when every word of `search` occurs (case-insensitively) in one of `fields`; an empty search matches all. */
export function matchesSearch(fields: readonly string[], search: string): boolean {
  const words = search.trim().toLocaleLowerCase().split(/\s+/).filter((word) => word !== '');
  const haystack = fields.join(' ').toLocaleLowerCase();
  return words.every((word) => haystack.includes(word));
}

/** The types whose key or names match `search`, in the server's order. */
export function filterLookupTypes(types: readonly LookupType[], search: string): readonly LookupType[] {
  return types.filter((type) => matchesSearch([type.type, type.nameAr, type.nameEn], search));
}

/** The values whose code or names match `search`, by sort order and then code (the order the portal shows). */
export function filterLookupItems(items: readonly AdminLookupItem[], search: string): readonly AdminLookupItem[] {
  return items
    .filter((item) => matchesSearch([item.code, item.nameAr, item.nameEn], search))
    .sort((left, right) => left.sortOrder - right.sortOrder || left.code.localeCompare(right.code));
}

/** The rows of 1-based `page` (clamped to the last page) of `rows`, `pageSize` rows a page. */
export function pageOf<T>(rows: readonly T[], page: number, pageSize: number): readonly T[] {
  const current = clampPage(page, rows.length, pageSize);
  const start = (current - 1) * pageSize;
  return rows.slice(start, start + pageSize);
}
