import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { AppLanguages } from '../../../core/i18n/language';
import { FakePortalContentClient, FakePortalTranslations, aContentSection, portalContentTestProviders } from '../portal.testing';
import { PortalContentStatus } from './portal-content';
import { PortalContentClient, PortalContentPages, type PortalContentDto } from './portal-content.client';
import { PortalContentStore } from './portal-content.store';

function setUp(content = new FakePortalContentClient(), translations = new FakePortalTranslations()) {
  TestBed.configureTestingModule({ providers: [...portalContentTestProviders(content, translations), PortalContentStore] });
  return { store: TestBed.inject(PortalContentStore), content, translations };
}

const page = (titleAr: string): PortalContentDto => ({
  page: PortalContentPages.Faq,
  updatedAt: null,
  sections: [aContentSection({ titleAr })],
});

describe('PortalContentStore', () => {
  it('starts loading and serves the sections in the active language once loaded', async () => {
    const { store, content, translations } = setUp();
    content.answer(PortalContentPages.Faq, [aContentSection({ titleAr: 'سؤال', titleEn: 'Question' })]);
    expect(store.status()).toBe(PortalContentStatus.Loading);

    await store.load(PortalContentPages.Faq);

    expect(store.status()).toBe(PortalContentStatus.Loaded);
    expect(store.sections().map(section => section.title)).toEqual(['سؤال']);
    translations.language.set(AppLanguages.English);
    expect(store.sections().map(section => section.title)).toEqual(['Question']);
    expect(store.empty()).toBe(false);
    expect(content.requests).toEqual([PortalContentPages.Faq]);
  });

  it('reports an empty page', async () => {
    const { store, content } = setUp();
    content.answer(PortalContentPages.Terms, []);
    await store.load(PortalContentPages.Terms);
    expect(store.empty()).toBe(true);
  });

  it('fails without rejecting, and a retry loads the same page again', async () => {
    const { store, content } = setUp();
    content.fail(PortalContentPages.Privacy);

    await expect(store.load(PortalContentPages.Privacy)).resolves.toBeUndefined();
    expect(store.status()).toBe(PortalContentStatus.Failed);
    expect(store.empty()).toBe(false);

    content.answer(PortalContentPages.Privacy, [aContentSection()]);
    await store.retry();
    expect(store.status()).toBe(PortalContentStatus.Loaded);
    expect(content.requests).toEqual([PortalContentPages.Privacy, PortalContentPages.Privacy]);
  });

  it('does nothing on retry before a first load', async () => {
    const { store, content } = setUp();
    await store.retry();
    expect(content.requests).toEqual([]);
  });

  it('ignores an answer that arrives after a newer load started', async () => {
    const first = new Subject<PortalContentDto>();
    const second = new Subject<PortalContentDto>();
    const answers = [first, second];
    TestBed.configureTestingModule({
      providers: [
        ...portalContentTestProviders(new FakePortalContentClient()),
        { provide: PortalContentClient, useValue: { page: () => answers.shift() } },
        PortalContentStore,
      ],
    });
    const store = TestBed.inject(PortalContentStore);

    const stale = store.load(PortalContentPages.Faq);
    const fresh = store.load(PortalContentPages.Faq);
    second.next(page('جديد'));
    second.complete();
    await fresh;
    first.next(page('قديم'));
    first.complete();
    await stale;

    expect(store.sections().map(section => section.title)).toEqual(['جديد']);
  });
});
