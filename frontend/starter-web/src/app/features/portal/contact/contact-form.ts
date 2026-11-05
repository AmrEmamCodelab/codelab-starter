import type { TranslationParams } from '../../../core/i18n/translation.service';
import { ContactTranslationKeys as Keys } from './contact-translation-keys';

/**
 * The contact topics. Lookup-style ids (the P5 backend seeds them as a bilingual lookup and this list moves to lookup
 * data); compare only through these constants.
 */
export const ContactTopicIds = {
  General: 1,
  Request: 2,
  Account: 3,
  Suggestion: 4,
} as const;

export type ContactTopicId = (typeof ContactTopicIds)[keyof typeof ContactTopicIds];

/** The topics in display order, with their label keys. */
export const CONTACT_TOPICS: readonly { readonly id: ContactTopicId; readonly labelKey: string }[] = [
  { id: ContactTopicIds.General, labelKey: Keys.TopicGeneral },
  { id: ContactTopicIds.Request, labelKey: Keys.TopicRequest },
  { id: ContactTopicIds.Account, labelKey: Keys.TopicAccount },
  { id: ContactTopicIds.Suggestion, labelKey: Keys.TopicSuggestion },
];

/** Field limits, in characters after trimming. The P5 validator of `POST api/portal/contact-messages` mirrors them. */
export const ContactLimits = {
  FullNameMaxLength: 100,
  EmailMaxLength: 254,
  MessageMinLength: 10,
  MessageMaxLength: 2000,
} as const;

/** The form fields, named as the API's camelCase body and problem+json `errors` keys. */
export const ContactFields = {
  FullName: 'fullName',
  Email: 'email',
  TopicId: 'topicId',
  Message: 'message',
} as const;

export type ContactField = (typeof ContactFields)[keyof typeof ContactFields];

/** What the reader typed. */
export interface ContactFormValue {
  readonly fullName: string;
  readonly email: string;
  readonly topicId: ContactTopicId | null;
  readonly message: string;
}

export const EMPTY_CONTACT_FORM: ContactFormValue = { fullName: '', email: '', topicId: null, message: '' };

/** One field message: its translation key and parameters. */
export interface ContactFieldMessage {
  readonly key: string;
  readonly params?: TranslationParams;
}

/** The client-side message of each invalid field (absent = valid). */
export type ContactFieldErrors = Partial<Record<ContactField, ContactFieldMessage>>;

/** A pragmatic address shape (a local part, one at-sign, a dotted domain, no spaces); the server has the final word. */
const EMAIL_SHAPE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

function fullNameError(fullName: string): ContactFieldMessage | undefined {
  if (fullName === '') return { key: Keys.FullNameRequired };
  if (fullName.length > ContactLimits.FullNameMaxLength) {
    return { key: Keys.FullNameTooLong, params: { max: ContactLimits.FullNameMaxLength } };
  }
  return undefined;
}

function emailError(email: string): ContactFieldMessage | undefined {
  if (email === '') return { key: Keys.EmailRequired };
  if (email.length > ContactLimits.EmailMaxLength) {
    return { key: Keys.EmailTooLong, params: { max: ContactLimits.EmailMaxLength } };
  }
  return EMAIL_SHAPE.test(email) ? undefined : { key: Keys.EmailInvalid };
}

function messageError(message: string): ContactFieldMessage | undefined {
  if (message === '') return { key: Keys.MessageRequired };
  if (message.length < ContactLimits.MessageMinLength) {
    return { key: Keys.MessageTooShort, params: { min: ContactLimits.MessageMinLength } };
  }
  if (message.length > ContactLimits.MessageMaxLength) {
    return { key: Keys.MessageTooLong, params: { max: ContactLimits.MessageMaxLength } };
  }
  return undefined;
}

/** The client-side checks of the form, on trimmed values. */
export function contactFormErrors(value: ContactFormValue): ContactFieldErrors {
  const errors: Record<string, ContactFieldMessage | undefined> = {
    [ContactFields.FullName]: fullNameError(value.fullName.trim()),
    [ContactFields.Email]: emailError(value.email.trim()),
    [ContactFields.TopicId]: value.topicId === null ? { key: Keys.TopicRequired } : undefined,
    [ContactFields.Message]: messageError(value.message.trim()),
  };
  return Object.fromEntries(Object.entries(errors).filter(([, message]) => message !== undefined)) as ContactFieldErrors;
}

export function hasContactErrors(errors: ContactFieldErrors): boolean {
  return Object.keys(errors).length > 0;
}

/** The field a server error key names (`fullName` and `FullName` alike), or null for a key of no form field. */
export function contactFieldOf(serverField: string): ContactField | null {
  const wanted = serverField.toLowerCase();
  return Object.values(ContactFields).find((field) => field.toLowerCase() === wanted) ?? null;
}
