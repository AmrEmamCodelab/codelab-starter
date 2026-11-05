import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { PortalLinks } from '../portal-links';
import { TEST_DIRECTIONS, portalTestProviders } from '../portal.testing';
import { PRIVACY_DOCUMENT, TERMS_DOCUMENT, type LegalDocument } from './legal-content';
import { LegalPageComponent } from './legal-page.component';
import { PrivacyTranslationKeys, TermsTranslationKeys } from './legal-translation-keys';

function render(document: LegalDocument): ComponentFixture<LegalPageComponent> {
  TestBed.configureTestingModule({ imports: [LegalPageComponent], providers: portalTestProviders() });
  const fixture = TestBed.createComponent(LegalPageComponent);
  fixture.componentRef.setInput('document', document);
  fixture.detectChanges();
  return fixture;
}

describe('LegalPageComponent', () => {
  afterEach(() => document.documentElement.removeAttribute('dir'));

  it.each(TEST_DIRECTIONS)('renders the privacy policy with one h1, its sections and lists (%s)', (direction) => {
    document.documentElement.setAttribute('dir', direction);
    const root: HTMLElement = render(PRIVACY_DOCUMENT).nativeElement;

    const headings = root.querySelectorAll('h1');
    expect(headings).toHaveLength(1);
    expect(headings[0]?.textContent?.trim()).toBe(PrivacyTranslationKeys.Title);
    const sections = root.querySelectorAll('section.legal__section');
    expect([...sections].map(section => section.id)).toEqual(PRIVACY_DOCUMENT.sections.map(section => `section-${section.id}`));
    expect(sections[0]?.querySelectorAll('li')).toHaveLength(3);
    expect(sections[1]?.querySelector('ul')).toBeNull();
  });

  it('builds the table of contents and the breadcrumb from the document', () => {
    const root: HTMLElement = render(TERMS_DOCUMENT).nativeElement;
    const tocLinks = [...root.querySelectorAll('.portal-content__toc-link')].map(link => link.textContent?.trim());
    expect(tocLinks).toEqual(TERMS_DOCUMENT.sections.map(section => section.titleKey));
    expect(root.querySelector('nav[aria-label]')?.textContent).toContain(TermsTranslationKeys.Title);
  });

  it('closes with a link to the contact page', () => {
    const root: HTMLElement = render(TERMS_DOCUMENT).nativeElement;
    expect(root.querySelector('.legal__contact-link')?.getAttribute('href')).toBe(PortalLinks.Contact);
  });
});
