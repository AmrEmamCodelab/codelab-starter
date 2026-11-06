import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { AppLanguages } from '../../../core/i18n/language';
import { PortalContentPages } from '../content/portal-content.client';
import { PortalContentTranslationKeys } from '../content/portal-content-translation-keys';
import { PortalLinks } from '../portal-links';
import {
  FakePortalContentClient,
  FakePortalTranslations,
  TEST_DIRECTIONS,
  aContentSection,
  portalContentTestProviders,
} from '../portal.testing';
import { PRIVACY_DOCUMENT, TERMS_DOCUMENT, type LegalDocument } from './legal-content';
import { LegalPageComponent } from './legal-page.component';
import { PrivacyTranslationKeys, TermsTranslationKeys } from './legal-translation-keys';

const SECTIONS = [
  aContentSection({ id: 'collect', titleAr: 'ما نجمعه', titleEn: 'What we collect', bodyEn: 'Your name.\n\nYour email.', sortOrder: 1 }),
  aContentSection({ id: 'use', titleAr: 'كيف نستخدمها', titleEn: 'How we use it', bodyEn: 'To answer you.', sortOrder: 2 }),
];

async function render(
  document: LegalDocument,
  content: FakePortalContentClient,
  translations = new FakePortalTranslations(),
): Promise<ComponentFixture<LegalPageComponent>> {
  TestBed.configureTestingModule({ imports: [LegalPageComponent], providers: portalContentTestProviders(content, translations) });
  const fixture = TestBed.createComponent(LegalPageComponent);
  fixture.componentRef.setInput('document', document);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture;
}

describe('LegalPageComponent', () => {
  afterEach(() => document.documentElement.removeAttribute('dir'));

  it.each(TEST_DIRECTIONS)('renders the privacy policy with one h1 and its API sections (%s)', async (direction, language) => {
    document.documentElement.setAttribute('dir', direction);
    const content = new FakePortalContentClient();
    content.answer(PortalContentPages.Privacy, SECTIONS);
    const translations = new FakePortalTranslations();
    translations.language.set(language);
    const root: HTMLElement = (await render(PRIVACY_DOCUMENT, content, translations)).nativeElement;

    expect(content.requests).toEqual([PortalContentPages.Privacy]);
    const headings = root.querySelectorAll('h1');
    expect(headings).toHaveLength(1);
    expect(headings[0]?.textContent?.trim()).toBe(PrivacyTranslationKeys.Title);
    const sections = root.querySelectorAll('section.legal__section');
    expect([...sections].map(section => section.id)).toEqual(['section-part-1', 'section-part-2']);
    expect(sections[0]?.querySelector('h2')?.textContent?.trim()).toBe(language === AppLanguages.Arabic ? 'ما نجمعه' : 'What we collect');
  });

  it('splits paragraphs and builds the table of contents and the breadcrumb', async () => {
    const content = new FakePortalContentClient();
    content.answer(PortalContentPages.Terms, SECTIONS);
    const translations = new FakePortalTranslations();
    translations.language.set(AppLanguages.English);
    const root: HTMLElement = (await render(TERMS_DOCUMENT, content, translations)).nativeElement;

    expect(content.requests).toEqual([PortalContentPages.Terms]);
    expect(root.querySelectorAll('section.legal__section')[0]?.querySelectorAll('.legal__paragraph')).toHaveLength(2);
    const tocLinks = [...root.querySelectorAll('.portal-content__toc-link')].map(link => link.textContent?.trim());
    expect(tocLinks).toEqual(['What we collect', 'How we use it']);
    expect(root.querySelector('nav[aria-label]')?.textContent).toContain(TermsTranslationKeys.Title);
  });

  it('shows the empty state and the closing contact link', async () => {
    const content = new FakePortalContentClient();
    content.answer(PortalContentPages.Terms, []);
    const root: HTMLElement = (await render(TERMS_DOCUMENT, content)).nativeElement;

    expect(root.querySelector('.dga-page-feedback__message')?.textContent).toBe(PortalContentTranslationKeys.Empty);
    expect(root.querySelector('.legal__contact-link')?.getAttribute('href')).toBe(PortalLinks.Contact);
  });
});
