import { describe, expect, it } from 'vitest';
import {
  chosenOptions,
  DgaListMoveKey,
  initialActiveOption,
  isChosen,
  isListMoveKey,
  isListToggleKey,
  moveActiveOption,
  NO_ACTIVE_OPTION,
  toggleValue,
  withoutValue,
} from './multi-select-state';

const same = Object.is;
const options = [
  { value: 'a', label: 'A', disabled: true },
  { value: 'b', label: 'B' },
  { value: 'c', label: 'C', disabled: true },
  { value: 'd', label: 'D' },
  { value: 'e', label: 'E', disabled: true },
];

describe('choosing values', () => {
  it('toggles a value on at the end and off again', () => {
    expect(toggleValue(['b'], 'd', same)).toEqual(['b', 'd']);
    expect(toggleValue(['b', 'd'], 'b', same)).toEqual(['d']);
  });

  it('removes every match and leaves the input untouched', () => {
    const chosen = ['b', 'd', 'b'];
    expect(withoutValue(chosen, 'b', same)).toEqual(['d']);
    expect(chosen).toEqual(['b', 'd', 'b']);
  });

  it('uses compareWith for object values', () => {
    const byId = (left: { id: number }, right: { id: number }) => left.id === right.id;
    expect(isChosen([{ id: 1 }], { id: 1 }, byId)).toBe(true);
    expect(toggleValue([{ id: 1 }], { id: 1 }, byId)).toEqual([]);
  });

  it('lists chosen options in option order and skips unknown values', () => {
    expect(chosenOptions(options, ['d', 'zz', 'b'], same).map((option) => option.value)).toEqual(['b', 'd']);
  });
});

describe('keys', () => {
  it('recognises move and toggle keys', () => {
    expect(isListMoveKey('ArrowDown')).toBe(true);
    expect(isListMoveKey('ArrowLeft')).toBe(false);
    expect(isListToggleKey(' ')).toBe(true);
    expect(isListToggleKey('Enter')).toBe(true);
    expect(isListToggleKey('a')).toBe(false);
  });
});

describe('moveActiveOption', () => {
  it('steps over disabled options and stays put at the ends', () => {
    expect(moveActiveOption(options, { from: 1, key: DgaListMoveKey.Next })).toBe(3);
    expect(moveActiveOption(options, { from: 3, key: DgaListMoveKey.Next })).toBe(3);
    expect(moveActiveOption(options, { from: 3, key: DgaListMoveKey.Previous })).toBe(1);
    expect(moveActiveOption(options, { from: 1, key: DgaListMoveKey.Previous })).toBe(1);
  });

  it('jumps to the first and last enabled option', () => {
    expect(moveActiveOption(options, { from: 3, key: DgaListMoveKey.First })).toBe(1);
    expect(moveActiveOption(options, { from: 1, key: DgaListMoveKey.Last })).toBe(3);
  });

  it('starts from the first enabled option when nothing is active', () => {
    expect(moveActiveOption(options, { from: NO_ACTIVE_OPTION, key: DgaListMoveKey.Next })).toBe(1);
    expect(moveActiveOption(options, { from: NO_ACTIVE_OPTION, key: DgaListMoveKey.Previous })).toBe(1);
  });

  it('has no active option when every option is disabled or the list is empty', () => {
    const disabled = [{ value: 1, label: 'x', disabled: true }];
    expect(moveActiveOption(disabled, { from: NO_ACTIVE_OPTION, key: DgaListMoveKey.Next })).toBe(NO_ACTIVE_OPTION);
    expect(moveActiveOption([], { from: NO_ACTIVE_OPTION, key: DgaListMoveKey.Last })).toBe(NO_ACTIVE_OPTION);
  });
});

describe('initialActiveOption', () => {
  it('prefers an enabled option, else the first enabled one', () => {
    expect(initialActiveOption(options, 3)).toBe(3);
    expect(initialActiveOption(options, 2)).toBe(1);
    expect(initialActiveOption(options, NO_ACTIVE_OPTION)).toBe(1);
  });
});
