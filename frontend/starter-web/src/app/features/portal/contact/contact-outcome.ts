import { HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import { DgaPageFeedbackKind } from '@codelab/ng-dga';
import { parseApiError } from '@codelab/ng-security';
import type { TranslationParams } from '../../../core/i18n/translation.service';
import { fieldMessageKey } from '../../../patterns';
import { contactFieldOf, type ContactField } from './contact-form';
import type { ContactMessageReceipt } from './portal-contact.client';
import { ContactTranslationKeys as Keys } from './contact-translation-keys';

/** Why a message was not sent. Integers, like every enum the starter owns. */
export enum ContactFailureKind {
  /** 400/422 with field errors: the reader corrects the highlighted fields. */
  Validation = 1,
  /** 429: too many messages in a short time. */
  RateLimited = 2,
  /** No response at all (status 0). */
  Offline = 3,
  /** Anything else (5xx, an unexpected status, a client-side failure). */
  Failed = 4,
}

/** The server's message keys (`errors.<CODE>`) of each form field it refused. */
export type ContactServerFieldErrors = Partial<Record<ContactField, readonly string[]>>;

export interface ContactFailure {
  readonly kind: ContactFailureKind;
  /** The short code to quote when the server sent one (or derived it from its trace id). */
  readonly referenceCode: string | null;
  readonly fieldErrors: ContactServerFieldErrors;
}

/** A page-level outcome, before translation. */
export interface ContactOutcome {
  readonly kind: DgaPageFeedbackKind;
  readonly titleKey: string;
  readonly messageKey: string;
  readonly params?: TranslationParams;
}

const VALIDATION_STATUSES: ReadonlySet<number> = new Set([HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity]);

function groupFieldErrors(fieldErrors: readonly { readonly field: string; readonly message: string }[]): ContactServerFieldErrors {
  const grouped: Partial<Record<ContactField, string[]>> = {};
  for (const { field, message } of fieldErrors) {
    const contactField = contactFieldOf(field);
    if (contactField !== null) {
      (grouped[contactField] ??= []).push(fieldMessageKey(message));
    }
  }
  return grouped;
}

/** Classifies what `PortalContactClient.send` threw. Never throws. */
export function contactFailureFrom(error: unknown): ContactFailure {
  if (!(error instanceof HttpErrorResponse)) {
    return { kind: ContactFailureKind.Failed, referenceCode: null, fieldErrors: {} };
  }
  const parsed = parseApiError(error);
  const referenceCode = parsed.referenceCode ?? null;
  if (VALIDATION_STATUSES.has(parsed.status) && parsed.fieldErrors.length > 0) {
    return { kind: ContactFailureKind.Validation, referenceCode, fieldErrors: groupFieldErrors(parsed.fieldErrors) };
  }
  if (parsed.status === HttpStatusCode.TooManyRequests) {
    return { kind: ContactFailureKind.RateLimited, referenceCode, fieldErrors: {} };
  }
  if (parsed.status === 0) {
    return { kind: ContactFailureKind.Offline, referenceCode, fieldErrors: {} };
  }
  return { kind: ContactFailureKind.Failed, referenceCode, fieldErrors: {} };
}

/** The banner for a sent message: with the reference number when the server returned one. */
export function successOutcome(receipt: ContactMessageReceipt | null): ContactOutcome {
  const reference = receipt?.referenceNumber?.trim() ?? '';
  return reference === ''
    ? { kind: DgaPageFeedbackKind.Success, titleKey: Keys.SuccessTitle, messageKey: Keys.SuccessBody }
    : {
        kind: DgaPageFeedbackKind.Success,
        titleKey: Keys.SuccessTitle,
        messageKey: Keys.SuccessBodyWithReference,
        params: { reference },
      };
}

const FAILURE_MESSAGE_KEYS: Readonly<Record<ContactFailureKind, string>> = {
  [ContactFailureKind.Validation]: Keys.ValidationFailed,
  [ContactFailureKind.RateLimited]: Keys.RateLimited,
  [ContactFailureKind.Offline]: Keys.Offline,
  [ContactFailureKind.Failed]: Keys.Failed,
};

/** The banner for a failure; a general failure with a reference code asks the reader to quote it. */
export function failureOutcome(failure: ContactFailure): ContactOutcome {
  const base = { kind: DgaPageFeedbackKind.Error, titleKey: Keys.FailureTitle };
  if (failure.kind === ContactFailureKind.Failed && failure.referenceCode !== null) {
    return { ...base, messageKey: Keys.FailedWithReference, params: { reference: failure.referenceCode } };
  }
  return { ...base, messageKey: FAILURE_MESSAGE_KEYS[failure.kind] };
}

/**
 * The key to show under a field the server refused: its first message that the bundles translate, else the generic
 * "check this field" (a code the app has no text for must never show as a raw key).
 *
 * @param isTranslated true when the active bundle holds the key.
 */
export function serverFieldMessageKey(messageKeys: readonly string[] | undefined, isTranslated: (key: string) => boolean): string | null {
  if (!messageKeys || messageKeys.length === 0) {
    return null;
  }
  return messageKeys.find(isTranslated) ?? Keys.FieldInvalid;
}
