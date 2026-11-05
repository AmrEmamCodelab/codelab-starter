import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { AppLanguages } from '../../../core/i18n/language';
import { SECTION_ID_PATTERN } from '../../../patterns';
import { PortalLinks } from '../portal-links';
import { FakePortalTranslations, TEST_DIRECTIONS, portalTestProviders } from '../portal.testing';
import {
  DOCUMENT_ITEM_KEYS,
  ELIGIBILITY_ITEM_KEYS,
  GUIDE_STEPS,
  SERVICE_GUIDE_SECTIONS,
  stepNumber,
} from './service-guide-content';
import { ServiceGuidePageComponent } from './service-guide-page.component';
import { ServiceGuideTranslationKeys as Keys } from './service-guide-translation-keys';

function render(translations = new FakePortalTranslations()): HTMLElement {
  TestBed.configureTestingModule({ imports: [ServiceGuidePageComponent], providers: portalTestProviders(translations) });
  const fixture = TestBed.createComponent(ServiceGuidePageComponent);
  fixture.detectChanges();
  return fixture.nativeElement;
}

describe('service guide content', () => {
  it('has unique, anchor-safe section ids', () => {
    const ids = SERVICE_GUIDE_SECTIONS.map(section => section.id);
    for (const id of ids) {
      expect(id).toMatch(SECTION_ID_PATTERN);
    }
    expect(new Set(ids).size).toBe(ids.length);
  });

  it('numbers steps from one in the language digits', () => {
    expect(stepNumber(0, AppLanguages.English)).toBe('1');
    expect(stepNumber(4, AppLanguages.English)).toBe('5');
    expect(stepNumber(1, AppLanguages.Arabic)).toBe(new Intl.NumberFormat(AppLanguages.Arabic).format(2));
  });
});

describe('ServiceGuidePageComponent', () => {
  afterEach(() => document.documentElement.removeAttribute('dir'));

  it.each(TEST_DIRECTIONS)('renders the title, every section and the lists (%s)', (direction) => {
    document.documentElement.setAttribute('dir', direction);
    const root = render();

    expect(root.querySelectorAll('h1')).toHaveLength(1);
    expect(root.querySelector('h1')?.textContent?.trim()).toBe(Keys.Title);
    expect([...root.querySelectorAll('section.guide__section')].map(section => section.id)).toEqual(
      SERVICE_GUIDE_SECTIONS.map(section => `section-${section.id}`),
    );
    const lists = root.querySelectorAll('.guide__checklist');
    expect(lists[0]?.querySelectorAll('li')).toHaveLength(ELIGIBILITY_ITEM_KEYS.length);
    expect(lists[1]?.querySelectorAll('li')).toHaveLength(DOCUMENT_ITEM_KEYS.length);
  });

  it('renders the steps as an ordered list with a spoken step number', () => {
    const translations = new FakePortalTranslations();
    translations.language.set(AppLanguages.English);
    const root = render(translations);

    const steps = root.querySelectorAll('ol.guide__steps > li');
    expect(steps).toHaveLength(GUIDE_STEPS.length);
    expect(steps[1]?.querySelector('.guide__step-number')?.getAttribute('aria-hidden')).toBe('true');
    expect(steps[1]?.querySelector('.guide__step-number')?.textContent?.trim()).toBe('2');
    expect(steps[1]?.querySelector('h3')?.textContent).toContain(`${Keys.StepNumber}|number=2`);
    expect(steps[1]?.querySelector('h3')?.textContent).toContain(GUIDE_STEPS[1]?.titleKey);
  });

  it('shows the before-you-submit note and links to the FAQ and contact pages', () => {
    const root = render();
    expect(root.querySelector('dga-alert')?.textContent).toContain(Keys.NoteBody);
    const links = [...root.querySelectorAll('.guide__help-link')].map(link => link.getAttribute('href'));
    expect(links).toEqual([PortalLinks.Faq, PortalLinks.Contact]);
  });
});
