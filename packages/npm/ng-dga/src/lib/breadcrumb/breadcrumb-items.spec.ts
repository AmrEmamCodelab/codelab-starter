import { describe, expect, it } from 'vitest';
import { resolveBreadcrumbs } from './breadcrumb-items';

describe('resolveBreadcrumbs', () => {
  it('makes the last crumb current when none is flagged and links the others that have a link', () => {
    const resolved = resolveBreadcrumbs([{ label: 'Home', link: '/' }, { label: 'Plain' }, { label: 'Admin', link: '/admin' }]);
    expect(resolved.map((item) => [item.isCurrent, item.isLink])).toEqual([
      [false, true],
      [false, false],
      [true, false],
    ]);
  });

  it('honours an explicitly flagged current crumb', () => {
    const resolved = resolveBreadcrumbs([{ label: 'Home', link: '/', current: true }, { label: 'Next', link: '/next' }]);
    expect(resolved.map((item) => [item.isCurrent, item.isLink])).toEqual([
      [true, false],
      [false, true],
    ]);
  });

  it('never makes a crumb flagged current: false the current page', () => {
    const resolved = resolveBreadcrumbs([{ label: 'Home', link: '/' }, { label: 'Last', link: '/last', current: false }]);
    expect(resolved.every((item) => !item.isCurrent)).toBe(true);
    expect(resolved[1].isLink).toBe(true);
  });

  it('returns an empty trail for no items', () => {
    expect(resolveBreadcrumbs([])).toEqual([]);
  });
});
