import { describe, expect, it } from 'vitest';
import { DGA_ICON_GLYPHS } from '../icon/dga-icon-names.generated';
import { alertIcon, alertRole, DgaAlertRole, DgaAlertVariant, isDgaIconName } from './alert-variant';

const VARIANTS = Object.values(DgaAlertVariant);

describe('alertRole', () => {
  it('interrupts for error and warning and is polite for info and success', () => {
    expect(alertRole(DgaAlertVariant.Error)).toBe(DgaAlertRole.Alert);
    expect(alertRole(DgaAlertVariant.Warning)).toBe(DgaAlertRole.Alert);
    expect(alertRole(DgaAlertVariant.Info)).toBe(DgaAlertRole.Status);
    expect(alertRole(DgaAlertVariant.Success)).toBe(DgaAlertRole.Status);
  });
});

describe('alertIcon', () => {
  it.each(VARIANTS)('gives %s an icon that exists in the icon font', (variant) => {
    const icon = alertIcon(variant);
    expect(icon).not.toBeNull();
    expect(DGA_ICON_GLYPHS[icon as keyof typeof DGA_ICON_GLYPHS]).toBeDefined();
  });

  it('prefers the variant glyph when the font has one', () => {
    for (const variant of VARIANTS) {
      if (isDgaIconName(variant)) expect(alertIcon(variant)).toBe(variant);
    }
  });
});

describe('isDgaIconName', () => {
  it('is true only for names in the icon font', () => {
    expect(isDgaIconName('info')).toBe(true);
    expect(isDgaIconName('no-such-icon')).toBe(false);
    expect(isDgaIconName('toString')).toBe(false);
  });
});
