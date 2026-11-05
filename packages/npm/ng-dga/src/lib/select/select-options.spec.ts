import { describe, expect, it } from 'vitest';
import {
  DGA_SELECT_EMPTY_VALUE,
  type DgaSelectOption,
  dgaSelectDefaultCompare,
  optionForNativeValue,
  optionNativeValue,
  selectedOptionIndex,
} from './select-options';

interface Region {
  readonly id: number;
}

const OPTIONS: readonly DgaSelectOption<Region>[] = [
  { value: { id: 1 }, label: 'One' },
  { value: { id: 2 }, label: 'Two' },
];
const sameId = (a: Region, b: Region) => a.id === b.id;

describe('selectedOptionIndex', () => {
  it('finds the option by the comparison', () => {
    expect(selectedOptionIndex(OPTIONS, { id: 2 }, sameId)).toBe(1);
  });

  it('uses identity by default, so a recreated object matches nothing', () => {
    expect(selectedOptionIndex(OPTIONS, OPTIONS[0].value, dgaSelectDefaultCompare)).toBe(0);
    expect(selectedOptionIndex(OPTIONS, { id: 1 }, dgaSelectDefaultCompare)).toBe(-1);
  });

  it('gives -1 for null', () => {
    expect(selectedOptionIndex(OPTIONS, null, sameId)).toBe(-1);
  });
});

describe('optionForNativeValue', () => {
  it('maps an index back to its option', () => {
    expect(optionForNativeValue(OPTIONS, optionNativeValue(1))).toBe(OPTIONS[1]);
  });

  it('gives null for the empty value, an unknown index and a non-index', () => {
    expect(optionForNativeValue(OPTIONS, DGA_SELECT_EMPTY_VALUE)).toBeNull();
    expect(optionForNativeValue(OPTIONS, '5')).toBeNull();
    expect(optionForNativeValue(OPTIONS, '1.5')).toBeNull();
    expect(optionForNativeValue(OPTIONS, 'abc')).toBeNull();
  });
});
