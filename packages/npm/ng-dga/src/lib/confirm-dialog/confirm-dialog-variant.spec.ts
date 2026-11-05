import { describe, expect, it } from 'vitest';
import { DGA_ICON_GLYPHS } from '../icon/dga-icon-names.generated';
import { confirmDialogIcon, confirmDialogInitialFocus, DgaConfirmDialogFocus, DgaConfirmDialogVariant } from './confirm-dialog-variant';

describe('confirmDialogIcon', () => {
  it.each(Object.values(DgaConfirmDialogVariant))('gives %s an icon that exists in the icon font', (variant) => {
    expect(DGA_ICON_GLYPHS[confirmDialogIcon(variant)]).toBeDefined();
  });

  it('warns for danger and asks otherwise', () => {
    expect(confirmDialogIcon(DgaConfirmDialogVariant.Danger)).toBe('warning');
    expect(confirmDialogIcon(DgaConfirmDialogVariant.Default)).toBe('question');
  });
});

describe('confirmDialogInitialFocus', () => {
  it('focuses Cancel for danger and Confirm otherwise', () => {
    expect(confirmDialogInitialFocus(DgaConfirmDialogVariant.Danger)).toBe(DgaConfirmDialogFocus.Cancel);
    expect(confirmDialogInitialFocus(DgaConfirmDialogVariant.Default)).toBe(DgaConfirmDialogFocus.Confirm);
  });
});
