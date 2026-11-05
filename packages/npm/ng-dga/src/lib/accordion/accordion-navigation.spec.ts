import { describe, expect, it } from 'vitest';
import { DgaAccordionKey, nextAccordionHeaderIndex } from './accordion-navigation';

const ALL_ENABLED = [false, false, false];
const MIDDLE_DISABLED = [false, true, false];

describe('nextAccordionHeaderIndex', () => {
  it('moves down and up, wrapping at the ends', () => {
    expect(nextAccordionHeaderIndex(ALL_ENABLED, 0, DgaAccordionKey.ArrowDown)).toBe(1);
    expect(nextAccordionHeaderIndex(ALL_ENABLED, 2, DgaAccordionKey.ArrowDown)).toBe(0);
    expect(nextAccordionHeaderIndex(ALL_ENABLED, 0, DgaAccordionKey.ArrowUp)).toBe(2);
  });

  it('jumps to the first and last enabled header with Home and End', () => {
    expect(nextAccordionHeaderIndex([true, false, false, true], 2, DgaAccordionKey.Home)).toBe(1);
    expect(nextAccordionHeaderIndex([true, false, false, true], 1, DgaAccordionKey.End)).toBe(2);
  });

  it('skips disabled headers', () => {
    expect(nextAccordionHeaderIndex(MIDDLE_DISABLED, 0, DgaAccordionKey.ArrowDown)).toBe(2);
    expect(nextAccordionHeaderIndex(MIDDLE_DISABLED, 2, DgaAccordionKey.ArrowUp)).toBe(0);
  });

  it('ignores other keys, an empty list and a list with no enabled header', () => {
    expect(nextAccordionHeaderIndex(ALL_ENABLED, 0, 'ArrowLeft')).toBeNull();
    expect(nextAccordionHeaderIndex([], 0, DgaAccordionKey.ArrowDown)).toBeNull();
    expect(nextAccordionHeaderIndex([true, true], 0, DgaAccordionKey.Home)).toBeNull();
  });
});
