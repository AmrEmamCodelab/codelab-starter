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
import { FaqPageComponent } from './faq-page.component';
import { FaqTranslationKeys as Keys } from './faq-translation-keys';

const QUESTIONS = [
  aContentSection({
    id: 'q-track',
    titleAr: 'كيف أتابع طلبي؟',
    titleEn: 'How do I follow my request?',
    bodyAr: 'سجّل الدخول وافتح طلباتي.',
    bodyEn: 'Sign in and open My requests.\n\nThe status shows on each request.',
    sortOrder: 1,
  }),
  aContentSection({
    id: 'q-password',
    titleAr: 'نسيت كلمة المرور',
    titleEn: 'I forgot my password',
    bodyAr: 'استخدم رابط إعادة التعيين.',
    bodyEn: 'Use the reset link.',
    sortOrder: 2,
  }),
];

interface Rendered {
  readonly fixture: ComponentFixture<FaqPageComponent>;
  readonly root: HTMLElement;
  readonly content: FakePortalContentClient;
  readonly translations: FakePortalTranslations;
}

const answerQuestions = (content: FakePortalContentClient): void => content.answer(PortalContentPages.Faq, QUESTIONS);

async function render(setUp: (content: FakePortalContentClient) => void = answerQuestions): Promise<Rendered> {
  const content = new FakePortalContentClient();
  setUp(content);
  const translations = new FakePortalTranslations();
  TestBed.configureTestingModule({ imports: [FaqPageComponent], providers: portalContentTestProviders(content, translations) });
  const fixture = TestBed.createComponent(FaqPageComponent);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return { fixture, root: fixture.nativeElement, content, translations };
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

const questionLabels = (root: HTMLElement): string[] =>
  [...root.querySelectorAll('.dga-accordion-item__header')].map(header => header.textContent?.trim() ?? '');

describe('FaqPageComponent', () => {
  afterEach(() => document.documentElement.removeAttribute('dir'));

  it.each(TEST_DIRECTIONS)('renders the questions from the API in one accordion, in the active language (%s)', async (direction, language) => {
    document.documentElement.setAttribute('dir', direction);
    const { fixture, root, content, translations } = await render();
    translations.language.set(language);
    fixture.detectChanges();

    expect(content.requests).toEqual([PortalContentPages.Faq]);
    expect(root.querySelectorAll('h1')).toHaveLength(1);
    expect(root.querySelectorAll('dga-accordion-item')).toHaveLength(QUESTIONS.length);
    const expected = language === AppLanguages.Arabic ? 'كيف أتابع طلبي؟' : 'How do I follow my request?';
    expect(questionLabels(root)[0]).toContain(expected);
    expect(root.querySelectorAll('.dga-accordion-item__header[aria-expanded="true"]')).toHaveLength(0);
    expect(root.querySelector('[data-testid="faq-result-count"]')?.textContent?.trim()).toBe('');
    expect(root.querySelector('.dga-page-feedback')).toBeNull();
  });

  it('renders each answer paragraph on its own', async () => {
    const { fixture, root, translations } = await render();
    translations.language.set(AppLanguages.English);
    fixture.detectChanges();
    const first = root.querySelector('dga-accordion-item');
    expect([...(first?.querySelectorAll('.faq__answer') ?? [])].map(paragraph => paragraph.textContent?.trim())).toEqual([
      'Sign in and open My requests.',
      'The status shows on each request.',
    ]);
  });

  it('filters as the reader types, opens the matches and announces the count', async () => {
    const { fixture, root, translations } = await render();
    translations.language.set(AppLanguages.English);
    search(fixture, 'follow request');

    expect(root.querySelectorAll('dga-accordion-item')).toHaveLength(1);
    expect(root.querySelector('.dga-accordion-item__header')?.getAttribute('aria-expanded')).toBe('true');
    expect(root.querySelector('[data-testid="faq-result-count"]')?.textContent?.trim()).toBe(`${Keys.ResultCount}|count=1`);
  });

  it('matches Arabic text without hamza or diacritics', async () => {
    const { fixture, root } = await render();
    search(fixture, 'اعاده التعيين');
    expect(root.querySelectorAll('dga-accordion-item')).toHaveLength(1);
  });

  it('shows the no-results state and clears the search', async () => {
    const { fixture, root } = await render();
    search(fixture, 'zzz-nothing');

    expect(root.querySelector('[data-testid="faq-no-results"]')?.textContent?.trim()).toBe(Keys.NoResults);
    expect(root.querySelectorAll('dga-accordion-item')).toHaveLength(0);

    const clear = root.querySelector<HTMLElement>('[data-testid="faq-clear-search"]');
    expect(clear?.hasAttribute('data-public-action')).toBe(true);
    clear?.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.query()).toBe('');
    expect(root.querySelectorAll('dga-accordion-item')).toHaveLength(QUESTIONS.length);
    expect(root.querySelector('[data-testid="faq-clear-search"]')).toBeNull();
  });

  it('shows the loading state and no search box while the questions load', async () => {
    const { root } = await render(() => undefined);
    expect(root.querySelector('.dga-page-feedback__message')?.textContent).toBe(PortalContentTranslationKeys.Loading);
    expect(root.querySelector('input[type="search"]')).toBeNull();
  });

  it('shows the empty state when there is no question', async () => {
    const { root } = await render(content => content.answer(PortalContentPages.Faq, []));
    expect(root.querySelector('.dga-page-feedback__message')?.textContent).toBe(PortalContentTranslationKeys.Empty);
    expect(root.querySelector('input[type="search"]')).toBeNull();
  });

  it('shows the error state with a retry that loads the questions again', async () => {
    const { fixture, root, content } = await render(failing => failing.fail(PortalContentPages.Faq));
    expect(root.querySelector('.dga-page-feedback')?.getAttribute('role')).toBe('alert');

    content.answer(PortalContentPages.Faq, QUESTIONS);
    root.querySelector<HTMLElement>('[data-testid="portal-content-retry"]')?.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(content.requests).toEqual([PortalContentPages.Faq, PortalContentPages.Faq]);
    expect(root.querySelectorAll('dga-accordion-item')).toHaveLength(QUESTIONS.length);
  });

  it('closes with a link to the contact page', async () => {
    const { root } = await render();
    expect(root.querySelector('.faq__help-link')?.getAttribute('href')).toBe(PortalLinks.Contact);
  });
});
