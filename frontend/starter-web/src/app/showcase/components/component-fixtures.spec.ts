import { DGA_ICON_GLYPHS, DgaFileUploadStatus, type DgaFileUploadItem } from '@codelab/ng-dga';
import { describe, expect, it } from 'vitest';
import {
  ICON_NAMES,
  NAV_FIXTURE,
  PICKED_ID_PREFIX,
  appendPicked,
  navItems,
  nextPickedNumber,
  removeItem,
  withLabels,
} from './component-fixtures';

const translate = (key: string) => `T(${key})`;
const file = (name: string) => new File(['x'], name);

describe('withLabels', () => {
  it('swaps each labelKey for its translation and keeps every other field', () => {
    expect(withLabels([{ labelKey: 'a', value: 1, disabled: true }], translate)).toEqual([{ label: 'T(a)', value: 1, disabled: true }]);
  });
});

describe('navItems', () => {
  it('translates every level and keeps links, ids and icons', () => {
    const items = navItems(NAV_FIXTURE, translate);
    expect(items.map(item => item.id)).toEqual(NAV_FIXTURE.map(item => item.id));
    const withChildren = items.find(item => item.children !== undefined);
    expect(withChildren?.children?.every(child => child.label.startsWith('T(') && child.link !== undefined)).toBe(true);
    expect(items.every(item => !('labelKey' in item))).toBe(true);
  });
});

describe('the upload demo list', () => {
  const uploaded: DgaFileUploadItem = { id: 'fixture', name: 'a.pdf', status: DgaFileUploadStatus.Uploaded };

  it('numbers picked rows from 0, after the highest picked number so far', () => {
    expect(nextPickedNumber([uploaded])).toBe(0);
    expect(nextPickedNumber([uploaded, { ...uploaded, id: `${PICKED_ID_PREFIX}4` }, { ...uploaded, id: `${PICKED_ID_PREFIX}1` }])).toBe(5);
  });

  it('appends every accepted file as a pending row', () => {
    const rows = appendPicked([uploaded], [file('one.pdf'), file('two.png')]);
    expect(rows).toEqual([
      uploaded,
      { id: `${PICKED_ID_PREFIX}0`, name: 'one.pdf', status: DgaFileUploadStatus.Queued },
      { id: `${PICKED_ID_PREFIX}1`, name: 'two.png', status: DgaFileUploadStatus.Queued },
    ]);
  });

  it('never reuses an id after a removal', () => {
    const first = appendPicked([], [file('one.pdf'), file('two.pdf')]);
    const afterRemoval = removeItem(first, `${PICKED_ID_PREFIX}0`);
    const next = appendPicked(afterRemoval, [file('three.pdf')]);
    const ids = next.map(row => row.id);
    expect(new Set(ids).size).toBe(ids.length);
    expect(ids).toEqual([`${PICKED_ID_PREFIX}1`, `${PICKED_ID_PREFIX}2`]);
  });
});

describe('ICON_NAMES', () => {
  it('lists every glyph of the icon font', () => {
    expect(ICON_NAMES).toEqual(Object.keys(DGA_ICON_GLYPHS));
    expect(ICON_NAMES.length).toBeGreaterThan(0);
  });
});
