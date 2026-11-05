import { describe, expect, it } from 'vitest';
import { DgaNumberIssue, formatNumberText, normalizeDigits, numberRangeIssue, parseNumberText } from './number-text';

describe('normalizeDigits', () => {
  it('turns Arabic-Indic and Extended Arabic-Indic digits and the Arabic decimal separator into Latin', () => {
    expect(normalizeDigits(' ١٢٣٫٥ ')).toBe('123.5');
    expect(normalizeDigits('۴۵۶')).toBe('456');
    expect(normalizeDigits('٠9')).toBe('09');
  });
});

describe('parseNumberText', () => {
  const decimal = { integer: false };
  const integer = { integer: true };

  it('reads decimals in either script', () => {
    expect(parseNumberText('12.5', decimal)).toBe(12.5);
    expect(parseNumberText('١٢٫٥', decimal)).toBe(12.5);
    expect(parseNumberText('-3', decimal)).toBe(-3);
    expect(parseNumberText('.5', decimal)).toBe(0.5);
    expect(parseNumberText('7.', decimal)).toBe(7);
  });

  it('gives undefined for empty text and NaN for anything else', () => {
    expect(parseNumberText('  ', decimal)).toBeUndefined();
    for (const text of ['abc', '1.2.3', '1e3', '--1', '.', '1,000']) expect(parseNumberText(text, decimal)).toBeNaN();
  });

  it('refuses a decimal point when integer is set', () => {
    expect(parseNumberText('١٤٤٦', integer)).toBe(1446);
    expect(parseNumberText('12.5', integer)).toBeNaN();
  });
});

describe('formatNumberText', () => {
  it('writes Latin digits and empty for null', () => {
    expect(formatNumberText(12.5)).toBe('12.5');
    expect(formatNumberText(null)).toBe('');
  });
});

describe('numberRangeIssue', () => {
  it('checks the inclusive range and never flags null', () => {
    const range = { min: 0, max: 100 };
    expect(numberRangeIssue(-1, range)).toBe(DgaNumberIssue.BelowMin);
    expect(numberRangeIssue(101, range)).toBe(DgaNumberIssue.AboveMax);
    expect(numberRangeIssue(0, range)).toBeNull();
    expect(numberRangeIssue(100, range)).toBeNull();
    expect(numberRangeIssue(null, range)).toBeNull();
    expect(numberRangeIssue(5000, { min: null, max: null })).toBeNull();
  });
});
