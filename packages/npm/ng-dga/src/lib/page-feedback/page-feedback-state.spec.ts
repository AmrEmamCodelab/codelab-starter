import { describe, expect, it } from 'vitest';
import { DGA_ICON_GLYPHS } from '../icon/dga-icon-names.generated';
import { DgaPageFeedbackKind, DgaPageFeedbackRole, pageFeedbackIcon, pageFeedbackRole } from './page-feedback-state';

describe('pageFeedbackRole', () => {
  it('interrupts for an error and is polite otherwise', () => {
    expect(pageFeedbackRole(DgaPageFeedbackKind.Error)).toBe(DgaPageFeedbackRole.Alert);
    expect(pageFeedbackRole(DgaPageFeedbackKind.Success)).toBe(DgaPageFeedbackRole.Status);
    expect(pageFeedbackRole(DgaPageFeedbackKind.Info)).toBe(DgaPageFeedbackRole.Status);
  });
});

describe('pageFeedbackIcon', () => {
  it.each(Object.values(DgaPageFeedbackKind))('gives %s its own glyph from the icon font', (kind) => {
    expect(pageFeedbackIcon(kind)).toBe(kind);
    expect(DGA_ICON_GLYPHS[pageFeedbackIcon(kind)]).toBeDefined();
  });
});
