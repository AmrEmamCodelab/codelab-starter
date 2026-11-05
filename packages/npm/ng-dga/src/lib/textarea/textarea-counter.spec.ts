import { describe, expect, it } from 'vitest';
import { DGA_TEXTAREA_DEFAULT_COUNTER_TEMPLATE, formatTextareaCounter } from './textarea-counter';

describe('formatTextareaCounter', () => {
  it('fills the default template with digits only', () => {
    expect(formatTextareaCounter(DGA_TEXTAREA_DEFAULT_COUNTER_TEMPLATE, { count: 12, max: 500 })).toBe('12 / 500');
  });

  it('fills every placeholder of a translated template, each as often as it appears', () => {
    expect(formatTextareaCounter('{count} من {max} — {remaining} ({count})', { count: 3, max: 10 })).toBe('3 من 10 — 7 (3)');
  });

  it('never reports a negative remainder', () => {
    expect(formatTextareaCounter('{remaining}', { count: 12, max: 10 })).toBe('0');
  });

  it('leaves a template without placeholders as it is', () => {
    expect(formatTextareaCounter('limit', { count: 1, max: 2 })).toBe('limit');
  });
});
