import { describe, expect, it } from 'vitest';
import { copyButtonAccessibleName, copyButtonLabel, showsCopyButton } from './error-reference';

const TEXTS = { referenceCode: '7Q2KD-M4XWB', copyLabel: 'Copy', copiedLabel: 'Copied' };

describe('showsCopyButton', () => {
  it('needs both a code and a copy label', () => {
    expect(showsCopyButton(TEXTS)).toBe(true);
    expect(showsCopyButton({ ...TEXTS, referenceCode: null })).toBe(false);
    expect(showsCopyButton({ ...TEXTS, referenceCode: '' })).toBe(false);
    expect(showsCopyButton({ ...TEXTS, copyLabel: null })).toBe(false);
  });
});

describe('copyButtonLabel', () => {
  it('reads copy, then copied after a copy', () => {
    expect(copyButtonLabel(TEXTS, false)).toBe('Copy');
    expect(copyButtonLabel(TEXTS, true)).toBe('Copied');
  });

  it('keeps the copy label when no copied label is given', () => {
    expect(copyButtonLabel({ ...TEXTS, copiedLabel: null }, true)).toBe('Copy');
  });

  it('is empty without a copy label', () => {
    expect(copyButtonLabel({ ...TEXTS, copyLabel: null }, false)).toBe('');
  });
});

describe('copyButtonAccessibleName', () => {
  it('uses the aria label when given', () => {
    expect(copyButtonAccessibleName({ ...TEXTS, copyAriaLabel: 'Copy the reference code' })).toBe('Copy the reference code');
  });

  it('leaves the visible label to name the button otherwise', () => {
    expect(copyButtonAccessibleName(TEXTS)).toBeNull();
    expect(copyButtonAccessibleName({ ...TEXTS, copyAriaLabel: null })).toBeNull();
    expect(copyButtonAccessibleName({ ...TEXTS, copyAriaLabel: '' })).toBeNull();
  });
});
