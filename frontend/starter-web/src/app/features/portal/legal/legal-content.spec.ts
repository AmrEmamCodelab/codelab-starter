import { describe, expect, it } from 'vitest';
import { PortalContentPages } from '../content/portal-content.client';
import { PRIVACY_DOCUMENT, TERMS_DOCUMENT } from './legal-content';

describe.each([
  ['privacy', PRIVACY_DOCUMENT, 'portal.privacy.', PortalContentPages.Privacy],
  ['terms', TERMS_DOCUMENT, 'portal.terms.', PortalContentPages.Terms],
])('the %s document', (_name, document, prefix, page) => {
  it('keeps its page texts in its own namespace', () => {
    expect(document.titleKey.startsWith(prefix)).toBe(true);
    expect(document.subtitleKey.startsWith(prefix)).toBe(true);
  });

  it('reads its sections from its own content page', () => {
    expect(document.page).toBe(page);
  });
});
