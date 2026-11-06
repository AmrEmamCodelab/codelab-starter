import { HttpErrorResponse } from '@angular/common/http';
import { DgaPageFeedbackKind } from '@codelab/ng-dga';
import { describe, expect, it } from 'vitest';
import { ContactFields } from './contact-form';
import {
  ContactFailureKind,
  contactFailureFrom,
  failureOutcome,
  PortalErrorCodes,
  serverFieldMessageKey,
  successOutcome,
} from './contact-outcome';
import ar from '../../../../assets/i18n/ar.json';
import en from '../../../../assets/i18n/en.json';
import { flattenTranslations } from '../../../core/i18n/translation.service';
import { ContactTranslationKeys as Keys } from './contact-translation-keys';

/** The server's code for a topic that is unknown or was deactivated after the page loaded. */
const TOPIC_NOT_FOUND_KEY = 'errors.PORTAL_TOPIC_NOT_FOUND';

function httpError(status: number, error: unknown = null): HttpErrorResponse {
  return new HttpErrorResponse({ status, error });
}

describe('contactFailureFrom', () => {
  it('reads a 400 problem+json with field errors as a validation failure on the form fields', () => {
    const failure = contactFailureFrom(httpError(400, {
      code: 'VALIDATION_FAILED',
      correlationId: 'trace-1',
      errors: { email: ['PORTAL_CONTACT_EMAIL_INVALID'], Message: ['errors.TOO_SHORT', 'PORTAL_X'], phone: ['IGNORED'] },
    }));
    expect(failure.kind).toBe(ContactFailureKind.Validation);
    expect(failure.fieldErrors).toEqual({
      [ContactFields.Email]: ['errors.PORTAL_CONTACT_EMAIL_INVALID'],
      [ContactFields.Message]: ['errors.TOO_SHORT', 'errors.PORTAL_X'],
    });
  });

  it('reads a 422 with field errors as validation too, and a 400 without them as a failure', () => {
    expect(contactFailureFrom(httpError(422, { errors: { fullName: ['X'] } })).kind).toBe(ContactFailureKind.Validation);
    expect(contactFailureFrom(httpError(400, { code: 'MALFORMED_REQUEST' })).kind).toBe(ContactFailureKind.Failed);
  });

  it('reads a 429 as rate-limited', () => {
    expect(contactFailureFrom(httpError(429, { code: 'RATE_LIMITED' }))).toEqual({
      kind: ContactFailureKind.RateLimited,
      referenceCode: null,
      fieldErrors: {},
    });
  });

  it('reads status 0 as offline', () => {
    expect(contactFailureFrom(httpError(0)).kind).toBe(ContactFailureKind.Offline);
  });

  it('keeps the reference code of a 500', () => {
    const failure = contactFailureFrom(httpError(500, { code: 'INTERNAL_ERROR', correlationId: 'abc', referenceCode: 'REF-42' }));
    expect(failure).toEqual({ kind: ContactFailureKind.Failed, referenceCode: 'REF-42', fieldErrors: {} });
  });

  it('reads anything that is not an HTTP error as a failure without a reference', () => {
    expect(contactFailureFrom(new Error('signing failed'))).toEqual({
      kind: ContactFailureKind.Failed,
      referenceCode: null,
      fieldErrors: {},
    });
  });
});

describe('successOutcome', () => {
  it('quotes the reference number when the server sent one', () => {
    expect(successOutcome({ referenceNumber: ' CM-1001 ' })).toEqual({
      kind: DgaPageFeedbackKind.Success,
      titleKey: Keys.SuccessTitle,
      messageKey: Keys.SuccessBodyWithReference,
      params: { reference: 'CM-1001' },
    });
  });

  it('thanks without a reference for an empty answer', () => {
    expect(successOutcome(null).messageKey).toBe(Keys.SuccessBody);
    expect(successOutcome({ referenceNumber: '' }).messageKey).toBe(Keys.SuccessBody);
  });
});

describe('failureOutcome', () => {
  it('maps each kind to its message', () => {
    expect(failureOutcome({ kind: ContactFailureKind.Validation, referenceCode: 'R', fieldErrors: {} }).messageKey).toBe(Keys.ValidationFailed);
    expect(failureOutcome({ kind: ContactFailureKind.RateLimited, referenceCode: null, fieldErrors: {} }).messageKey).toBe(Keys.RateLimited);
    expect(failureOutcome({ kind: ContactFailureKind.Offline, referenceCode: null, fieldErrors: {} }).messageKey).toBe(Keys.Offline);
    expect(failureOutcome({ kind: ContactFailureKind.Failed, referenceCode: null, fieldErrors: {} }).messageKey).toBe(Keys.Failed);
  });

  it('asks to quote the reference code of a general failure', () => {
    expect(failureOutcome({ kind: ContactFailureKind.Failed, referenceCode: 'REF-42', fieldErrors: {} })).toEqual({
      kind: DgaPageFeedbackKind.Error,
      titleKey: Keys.FailureTitle,
      messageKey: Keys.FailedWithReference,
      params: { reference: 'REF-42' },
    });
  });
});

describe('serverFieldMessageKey', () => {
  const translated = new Set(['errors.KNOWN']);
  const isTranslated = (key: string): boolean => translated.has(key);

  it('is null without messages', () => {
    expect(serverFieldMessageKey(undefined, isTranslated)).toBeNull();
    expect(serverFieldMessageKey([], isTranslated)).toBeNull();
  });

  it('picks the first translated message, else the generic one', () => {
    expect(serverFieldMessageKey(['errors.UNKNOWN', 'errors.KNOWN'], isTranslated)).toBe('errors.KNOWN');
    expect(serverFieldMessageKey(['errors.UNKNOWN'], isTranslated)).toBe(Keys.FieldInvalid);
  });
});

describe('a retired topic', () => {
  it('shows the topic error from the server under the topic field', () => {
    const failure = contactFailureFrom(httpError(400, { errors: { topicId: ['PORTAL_TOPIC_NOT_FOUND'] } }));
    expect(failure.fieldErrors).toEqual({ [ContactFields.TopicId]: [TOPIC_NOT_FOUND_KEY] });
    expect(serverFieldMessageKey(failure.fieldErrors[ContactFields.TopicId], () => true)).toBe(TOPIC_NOT_FOUND_KEY);
  });

  it.each([
    ['ar', ar, 'لم يعد هذا الموضوع متاحًا. اختر موضوعًا آخر.'],
    ['en', en, 'This topic is no longer available. Choose another one.'],
  ])('says in %s that the topic is no longer available, not to pick a listed one', (_language, bundle, text) => {
    expect(flattenTranslations(bundle)[TOPIC_NOT_FOUND_KEY]).toBe(text);
  });
});

describe('a closed contact form (409 PORTAL_CONTACT_DISABLED)', () => {
  const CLOSED_KEY = 'errors.PORTAL_CONTACT_DISABLED';
  const closedError = (): HttpErrorResponse =>
    httpError(409, { code: PortalErrorCodes.ContactDisabled, correlationId: 'trace-9', referenceCode: 'REF-9' });

  it('reads the 409 with the disabled code as closed, not as a failure', () => {
    expect(contactFailureFrom(closedError()).kind).toBe(ContactFailureKind.Closed);
  });

  it('reads a 409 with any other code as a general failure', () => {
    expect(contactFailureFrom(httpError(409, { code: 'SOMETHING_ELSE' })).kind).toBe(ContactFailureKind.Failed);
    expect(contactFailureFrom(httpError(409)).kind).toBe(ContactFailureKind.Failed);
  });

  it('shows an info banner with the server message and no reference code to quote', () => {
    expect(failureOutcome(contactFailureFrom(closedError()))).toEqual({
      kind: DgaPageFeedbackKind.Info,
      titleKey: Keys.ClosedTitle,
      messageKey: CLOSED_KEY,
    });
  });

  it.each([
    ['ar', ar],
    ['en', en],
  ])('has %s text for the banner title and message', (_language, bundle) => {
    const flat = flattenTranslations(bundle);
    expect(flat[Keys.ClosedTitle]).toBeTruthy();
    expect(flat[CLOSED_KEY]).toBeTruthy();
  });
});
