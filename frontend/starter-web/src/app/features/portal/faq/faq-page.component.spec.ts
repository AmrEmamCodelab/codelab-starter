import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { PortalLinks } from '../portal-links';
import { FakePortalTranslations, TEST_DIRECTIONS, portalTestProviders } from '../portal.testing';
import { FAQ_CATEGORIES, faqEntryCount } from './faq-content';
import { FaqPageComponent } from './faq-page.component';
import { FaqTranslationKeys as Keys } from './faq-translation-keys';

function render(): { fixture: ComponentFixture<FaqPageComponent>; root: HTMLElement } {
  const translations = new FakePortalTranslations();
  translations.define({
    [Keys.TrackQuestion]: 'How do I follow my request?',
    [Keys.TrackAnswer]: 'Sign in and open My requests.',
    [Keys.PasswordQuestion]: 'نسيت كلمة المرور',
    [Keys.PasswordAnswer]: 'استخدم رابط إعادة التعيين.',
  });
  TestBed.configureTestingModule({ imports: [FaqPageComponent], providers: portalTestProviders(translations) });
  const fixture = TestBed.createComponent(FaqPageComponent);
  fixture.detectChanges();
  return { fixture, root: fixture.nativeElement };
}

function search(fixture: ComponentFixture<FaqPageComponent>, text: string): void {
  const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('input[type="search"]');
  if (!input) {
    throw new Error('no search box');
  }
  input.value = text;
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

describe('FaqPageComponent', () => {
  afterEach(() => document.documentElement.removeAttribute('dir'));

  it.each(TEST_DIRECTIONS)('renders every category as a section with one accordion (%s)', (direction) => {
    document.documentElement.setAttribute('dir', direction);
    const { root } = render();

    expect(root.querySelectorAll('h1')).toHaveLength(1);
    const sections = root.querySelectorAll('section.faq__category');
    expect([...sections].map(section => section.id)).toEqual(FAQ_CATEGORIES.map(category => `section-${category.id}`));
    expect(root.querySelectorAll('dga-accordion-item')).toHaveLength(faqEntryCount(FAQ_CATEGORIES));
    expect(root.querySelectorAll('.dga-accordion-item__header[aria-expanded="true"]')).toHaveLength(0);
    expect(root.querySelector('[data-testid="faq-result-count"]')?.textContent?.trim()).toBe('');
  });

  it('filters as the reader types, opens the matches and announces the count', () => {
    const { fixture, root } = render();
    search(fixture, 'follow request');

    expect(root.querySelectorAll('dga-accordion-item')).toHaveLength(1);
    expect(root.querySelectorAll('section.faq__category')).toHaveLength(1);
    expect(root.querySelector('.dga-accordion-item__header')?.getAttribute('aria-expanded')).toBe('true');
    expect(root.querySelector('[data-testid="faq-result-count"]')?.textContent?.trim()).toBe(`${Keys.ResultCount}|count=1`);
    expect(root.querySelectorAll('.portal-content__toc-link')).toHaveLength(0);
  });

  it('matches Arabic text without hamza or diacritics', () => {
    const { fixture, root } = render();
    search(fixture, 'اعاده التعيين');
    expect(root.querySelectorAll('dga-accordion-item')).toHaveLength(1);
  });

  it('shows the no-results state and clears the search', () => {
    const { fixture, root } = render();
    search(fixture, 'zzz-nothing');

    expect(root.querySelector('[data-testid="faq-no-results"]')?.textContent?.trim()).toBe(Keys.NoResults);
    expect(root.querySelectorAll('section.faq__category')).toHaveLength(0);

    const clear = root.querySelector<HTMLElement>('[data-testid="faq-clear-search"]');
    expect(clear?.hasAttribute('data-public-action')).toBe(true);
    clear?.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.query()).toBe('');
    expect(root.querySelectorAll('dga-accordion-item')).toHaveLength(faqEntryCount(FAQ_CATEGORIES));
    expect(root.querySelector('[data-testid="faq-clear-search"]')).toBeNull();
  });

  it('closes with a link to the contact page', () => {
    const { root } = render();
    expect(root.querySelector('.faq__help-link')?.getAttribute('href')).toBe(PortalLinks.Contact);
  });
});
