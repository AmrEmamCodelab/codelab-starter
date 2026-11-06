import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { AppLanguages } from '../../../core/i18n/language';
import { PortalContentPages } from '../content/portal-content.client';
import { PortalLinks } from '../portal-links';
import {
  FakePortalContentClient,
  FakePortalTranslations,
  TEST_DIRECTIONS,
  aContentSection,
  portalContentTestProviders,
} from '../portal.testing';
import { GUIDE_STEPS, ServiceGuideSectionIds } from './service-guide-content';
import { ServiceGuidePageComponent } from './service-guide-page.component';
import { ServiceGuideTranslationKeys as Keys } from './service-guide-translation-keys';

const SECTIONS = [
  aContentSection({ id: 'docs', titleAr: 'المستندات', titleEn: 'Documents', bodyEn: 'Prepare these.\n\nBring originals.', sortOrder: 2 }),
  aContentSection({ id: 'about', titleAr: 'عن الخدمة', titleEn: 'About the service', bodyEn: 'What it is.', sortOrder: 1 }),
];

async function render(content = new FakePortalContentClient(), translations = new FakePortalTranslations()): Promise<HTMLElement> {
  TestBed.configureTestingModule({
    imports: [ServiceGuidePageComponent],
    providers: portalContentTestProviders(content, translations),
  });
  const fixture = TestBed.createComponent(ServiceGuidePageComponent);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture.nativeElement;
}

describe('service guide content', () => {
  it('keeps the static steps the landing page counts', () => {
    expect(GUIDE_STEPS.length).toBeGreaterThan(0);
  });
});

describe('ServiceGuidePageComponent', () => {
  afterEach(() => document.documentElement.removeAttribute('dir'));

  it.each(TEST_DIRECTIONS)('renders the title and the API sections in order, then the help block (%s)', async (direction, language) => {
    document.documentElement.setAttribute('dir', direction);
    const content = new FakePortalContentClient();
    content.answer(PortalContentPages.ServiceGuide, SECTIONS);
    const translations = new FakePortalTranslations();
    translations.language.set(language);
    const root = await render(content, translations);

    expect(content.requests).toEqual([PortalContentPages.ServiceGuide]);
    expect(root.querySelectorAll('h1')).toHaveLength(1);
    expect(root.querySelector('h1')?.textContent?.trim()).toBe(Keys.Title);
    expect([...root.querySelectorAll('section.guide__section')].map(section => section.id)).toEqual([
      'section-part-1',
      'section-part-2',
      `section-${ServiceGuideSectionIds.Help}`,
    ]);
    const headings = [...root.querySelectorAll('section.guide__section h2')].map(heading => heading.textContent?.trim());
    expect(headings.slice(0, 2)).toEqual(language === AppLanguages.Arabic ? ['عن الخدمة', 'المستندات'] : ['About the service', 'Documents']);
  });

  it('renders each paragraph on its own and lists the sections in the table of contents', async () => {
    const content = new FakePortalContentClient();
    content.answer(PortalContentPages.ServiceGuide, SECTIONS);
    const translations = new FakePortalTranslations();
    translations.language.set(AppLanguages.English);
    const root = await render(content, translations);

    const second = root.querySelectorAll('section.guide__section')[1];
    expect([...(second?.querySelectorAll('.guide__paragraph') ?? [])].map(paragraph => paragraph.textContent?.trim())).toEqual([
      'Prepare these.',
      'Bring originals.',
    ]);
    const tocLinks = [...root.querySelectorAll('.portal-content__toc-link')].map(link => link.textContent?.trim());
    expect(tocLinks).toEqual(['About the service', 'Documents', Keys.HelpTitle]);
  });

  it('shows the error state and still offers the help links', async () => {
    const content = new FakePortalContentClient();
    content.fail(PortalContentPages.ServiceGuide);
    const root = await render(content);

    expect(root.querySelector('.dga-page-feedback')?.getAttribute('role')).toBe('alert');
    expect(root.querySelector('[data-testid="portal-content-retry"]')).not.toBeNull();
    const links = [...root.querySelectorAll('.guide__help-link')].map(link => link.getAttribute('href'));
    expect(links).toEqual([PortalLinks.Faq, PortalLinks.Contact]);
  });
});
