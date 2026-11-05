import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { DgaPageFeedbackKind } from '@codelab/ng-dga';
import { NEVER, of, throwError, type Observable } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiService } from '../../../core/api/api.service';
import { AppLanguages } from '../../../core/i18n/language';
import { TranslationService } from '../../../core/i18n/translation.service';
import { FakePortalTranslations } from '../portal.testing';
import { ContactFields, ContactTopicIds, EMPTY_CONTACT_FORM, type ContactFormValue } from './contact-form';
import { ContactTranslationKeys as Keys } from './contact-translation-keys';
import { PortalApiPaths } from './portal-contact.client';
import { PortalContactStore } from './portal-contact.store';

const VALID: ContactFormValue = {
  fullName: '  Sara Ali ',
  email: ' sara@example.test ',
  topicId: ContactTopicIds.Request,
  message: '  Where can I see my request?  ',
};

describe('PortalContactStore', () => {
  let post: ReturnType<typeof vi.fn<(path: string, body: unknown) => Observable<unknown>>>;
  let translations: FakePortalTranslations;
  let store: PortalContactStore;

  beforeEach(() => {
    post = vi.fn<(path: string, body: unknown) => Observable<unknown>>();
    translations = new FakePortalTranslations();
    translations.language.set(AppLanguages.English);
    TestBed.configureTestingModule({
      providers: [
        PortalContactStore,
        { provide: ApiService, useValue: { post } },
        { provide: TranslationService, useValue: translations },
      ],
    });
    store = TestBed.inject(PortalContactStore);
  });

  it('sends the trimmed message with the active language and shows the reference number', async () => {
    post.mockReturnValue(of({ referenceNumber: 'CM-7' }));

    await expect(store.send(VALID)).resolves.toBe(true);

    expect(post).toHaveBeenCalledWith(PortalApiPaths.Contact, {
      fullName: 'Sara Ali',
      email: 'sara@example.test',
      topicId: ContactTopicIds.Request,
      message: 'Where can I see my request?',
      language: AppLanguages.English,
    }, expect.anything());
    expect(store.outcome()).toEqual({
      kind: DgaPageFeedbackKind.Success,
      titleKey: Keys.SuccessTitle,
      messageKey: Keys.SuccessBodyWithReference,
      params: { reference: 'CM-7' },
    });
    expect(store.busy()).toBe(false);
  });

  it('refuses to send an invalid form or a second message while one is pending', async () => {
    await expect(store.send(EMPTY_CONTACT_FORM)).resolves.toBe(false);
    expect(post).not.toHaveBeenCalled();

    post.mockReturnValue(NEVER);
    void store.send(VALID);
    expect(store.busy()).toBe(true);
    await expect(store.send(VALID)).resolves.toBe(false);
    expect(post).toHaveBeenCalledTimes(1);
  });

  it('keeps the server field errors of a 400 problem+json and clears them per field', async () => {
    post.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 400,
      error: { code: 'VALIDATION_FAILED', errors: { email: ['PORTAL_CONTACT_EMAIL_BLOCKED'], message: ['PORTAL_X'] } },
    })));

    await expect(store.send(VALID)).resolves.toBe(false);

    expect(store.serverFieldErrors()).toEqual({
      [ContactFields.Email]: ['errors.PORTAL_CONTACT_EMAIL_BLOCKED'],
      [ContactFields.Message]: ['errors.PORTAL_X'],
    });
    expect(store.outcome()?.messageKey).toBe(Keys.ValidationFailed);
    expect(store.outcome()?.kind).toBe(DgaPageFeedbackKind.Error);

    store.clearServerError(ContactFields.Email);
    expect(store.serverFieldErrors()).toEqual({ [ContactFields.Message]: ['errors.PORTAL_X'] });
    store.clearServerError(ContactFields.FullName);
    expect(store.serverFieldErrors()).toEqual({ [ContactFields.Message]: ['errors.PORTAL_X'] });
  });

  it('shows the rate-limit message on 429', async () => {
    post.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 429 })));
    await store.send(VALID);
    expect(store.outcome()?.messageKey).toBe(Keys.RateLimited);
    expect(store.serverFieldErrors()).toEqual({});
  });

  it('shows the reference code of a 500', async () => {
    post.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 500,
      error: { code: 'INTERNAL_ERROR', correlationId: 'trace', referenceCode: 'REF-9' },
    })));
    await store.send(VALID);
    expect(store.outcome()).toEqual({
      kind: DgaPageFeedbackKind.Error,
      titleKey: Keys.FailureTitle,
      messageKey: Keys.FailedWithReference,
      params: { reference: 'REF-9' },
    });
    expect(store.busy()).toBe(false);
  });

  it('clears the previous outcome when sending again and on dismiss', async () => {
    post.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 429 })));
    await store.send(VALID);
    post.mockReturnValue(NEVER);
    void store.send(VALID);
    expect(store.outcome()).toBeNull();

    post.mockReturnValue(of(null));
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [PortalContactStore, { provide: ApiService, useValue: { post } }, { provide: TranslationService, useValue: translations }],
    });
    const fresh = TestBed.inject(PortalContactStore);
    await fresh.send(VALID);
    expect(fresh.outcome()?.messageKey).toBe(Keys.SuccessBody);
    fresh.clearOutcome();
    expect(fresh.outcome()).toBeNull();
  });
});
