import { describe, expect, it } from 'vitest';
import { cardValueText, renderCards } from './record-cards';
import { formatRecordInstant, instantMs } from './record-instant';

describe('record cards', () => {
  it.each([
    ['text', 'text'],
    ['  padded  ', 'padded'],
    [42, '42'],
    [0, '0'],
  ])('shows %j as %j', (value, text) => {
    expect(cardValueText(value)).toBe(text);
  });

  it.each([null, undefined, '', '   ', Number.NaN, Number.POSITIVE_INFINITY])('treats %j as empty', (value) => {
    expect(cardValueText(value)).toBeNull();
  });

  it('keeps adapter order and leaves out a card without fields', () => {
    const rendered = renderCards([
      { titleKey: 'a', fields: [{ labelKey: 'a.one', value: 'x' }, { labelKey: 'a.two', value: null }] },
      { titleKey: 'empty', fields: [] },
      { titleKey: 'b', fields: [{ labelKey: 'b.one', value: 7 }] },
    ]);
    expect(rendered.map((item) => item.card.titleKey)).toEqual(['a', 'b']);
    expect(rendered[0]?.fields.map((field) => field.text)).toEqual(['x', null]);
    expect(rendered[1]?.fields[0]?.text).toBe('7');
  });
});

describe('record instants', () => {
  it('formats a readable instant in the language and keeps an unreadable one as it is', () => {
    expect(formatRecordInstant('2026-11-06T10:00:00Z', 'en')).toMatch(/2026/);
    expect(formatRecordInstant('not-a-date', 'ar')).toBe('not-a-date');
  });

  it('sorts an unreadable instant as the oldest', () => {
    expect(instantMs('garbage')).toBe(Number.NEGATIVE_INFINITY);
    expect(instantMs('2026-11-06T00:00:00Z')).toBe(Date.UTC(2026, 10, 6));
  });
});
