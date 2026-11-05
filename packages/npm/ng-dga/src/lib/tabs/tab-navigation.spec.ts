import { describe, expect, it } from 'vitest';
import { type DgaTab, DgaTabKey, dgaTabPanelId, focusableTabIndex, indexOfTab, nextTabIndex } from './tab-navigation';

const TABS: readonly DgaTab[] = [
  { value: 'a', label: 'A' },
  { value: 'b', label: 'B', disabled: true },
  { value: 'c', label: 'C' },
  { value: 'd', label: 'D' },
];

describe('tab navigation', () => {
  it('builds panel ids and finds tabs by value', () => {
    expect(dgaTabPanelId('records', 'c')).toBe('records-c');
    expect(indexOfTab(TABS, 'c')).toBe(2);
    expect(indexOfTab(TABS, 'zz')).toBe(-1);
  });

  it('focuses the selected tab, else the first enabled tab', () => {
    expect(focusableTabIndex(TABS, 'c')).toBe(2);
    expect(focusableTabIndex(TABS, 'b')).toBe(0);
    expect(focusableTabIndex(TABS, 'missing')).toBe(0);
    expect(focusableTabIndex([{ value: 1, label: 'x', disabled: true }], 1)).toBeNull();
  });

  it('moves forward and backward in LTR, skipping disabled tabs and wrapping', () => {
    expect(nextTabIndex(TABS, 0, DgaTabKey.ArrowRight, false)).toBe(2);
    expect(nextTabIndex(TABS, 3, DgaTabKey.ArrowRight, false)).toBe(0);
    expect(nextTabIndex(TABS, 2, DgaTabKey.ArrowLeft, false)).toBe(0);
    expect(nextTabIndex(TABS, 0, DgaTabKey.ArrowLeft, false)).toBe(3);
  });

  it('mirrors the arrow keys in RTL', () => {
    expect(nextTabIndex(TABS, 0, DgaTabKey.ArrowLeft, true)).toBe(2);
    expect(nextTabIndex(TABS, 2, DgaTabKey.ArrowRight, true)).toBe(0);
  });

  it('jumps to the first and last enabled tab and ignores other keys', () => {
    expect(nextTabIndex(TABS, 2, DgaTabKey.Home, false)).toBe(0);
    expect(nextTabIndex(TABS, 0, DgaTabKey.End, false)).toBe(3);
    expect(nextTabIndex(TABS, 0, 'Enter', false)).toBeNull();
    expect(nextTabIndex([], 0, DgaTabKey.Home, false)).toBeNull();
  });
});
