import { describe, expect, it } from 'vitest';
import { hasText, joinIds } from './aria';
import { isRightToLeft } from './direction';
import { createDgaId } from './unique-id';

describe('joinIds', () => {
  it('joins the present ids with spaces', () => {
    expect(joinIds(['a', null, 'b', undefined, ''])).toBe('a b');
  });

  it('returns null when no id is present', () => {
    expect(joinIds([null, undefined, ''])).toBeNull();
  });
});

describe('hasText', () => {
  it('is true only for a non-empty string', () => {
    expect(hasText('x')).toBe(true);
    expect(hasText('')).toBe(false);
    expect(hasText(null)).toBe(false);
    expect(hasText(undefined)).toBe(false);
  });
});

describe('createDgaId', () => {
  it('prefixes the id and never repeats it', () => {
    const first = createDgaId('dga-test');
    const second = createDgaId('dga-test');
    expect(first.startsWith('dga-test-')).toBe(true);
    expect(second).not.toBe(first);
  });
});

describe('isRightToLeft', () => {
  it('follows the nearest dir attribute, case-insensitively', () => {
    const outer = document.createElement('div');
    outer.setAttribute('dir', 'RTL');
    const inner = document.createElement('span');
    outer.appendChild(inner);
    expect(isRightToLeft(inner)).toBe(true);
    inner.setAttribute('dir', 'ltr');
    expect(isRightToLeft(inner)).toBe(false);
  });

  it('is false without any dir attribute', () => {
    expect(isRightToLeft(document.createElement('span'))).toBe(false);
  });
});
