import { describe, expect, it } from 'vitest';
import {
  CONTACT_TOPICS,
  ContactFields,
  ContactLimits,
  ContactTopicIds,
  EMPTY_CONTACT_FORM,
  contactFieldOf,
  contactFormErrors,
  hasContactErrors,
  type ContactFormValue,
} from './contact-form';
import { ContactTranslationKeys as Keys } from './contact-translation-keys';

const VALID: ContactFormValue = {
  fullName: 'Sara Ali',
  email: 'sara@example.test',
  topicId: ContactTopicIds.General,
  message: 'I would like to know more about the service.',
};

describe('contactFormErrors', () => {
  it('passes a complete form', () => {
    expect(contactFormErrors(VALID)).toEqual({});
    expect(hasContactErrors(contactFormErrors(VALID))).toBe(false);
  });

  it('requires every field of the empty form', () => {
    const errors = contactFormErrors(EMPTY_CONTACT_FORM);
    expect(errors).toEqual({
      [ContactFields.FullName]: { key: Keys.FullNameRequired },
      [ContactFields.Email]: { key: Keys.EmailRequired },
      [ContactFields.TopicId]: { key: Keys.TopicRequired },
      [ContactFields.Message]: { key: Keys.MessageRequired },
    });
    expect(hasContactErrors(errors)).toBe(true);
  });

  it('treats whitespace-only values as empty', () => {
    const errors = contactFormErrors({ ...VALID, fullName: '   ', message: ' \n ' });
    expect(errors[ContactFields.FullName]?.key).toBe(Keys.FullNameRequired);
    expect(errors[ContactFields.Message]?.key).toBe(Keys.MessageRequired);
  });

  it('checks the email shape and length', () => {
    expect(contactFormErrors({ ...VALID, email: 'sara@' })[ContactFields.Email]?.key).toBe(Keys.EmailInvalid);
    expect(contactFormErrors({ ...VALID, email: 'sara @example.test' })[ContactFields.Email]?.key).toBe(Keys.EmailInvalid);
    expect(contactFormErrors({ ...VALID, email: '  sara@example.test ' })).toEqual({});
    const long = `${'a'.repeat(ContactLimits.EmailMaxLength)}@example.test`;
    expect(contactFormErrors({ ...VALID, email: long })[ContactFields.Email]).toEqual({
      key: Keys.EmailTooLong,
      params: { max: ContactLimits.EmailMaxLength },
    });
  });

  it('checks the name length at the limit', () => {
    expect(contactFormErrors({ ...VALID, fullName: 'a'.repeat(ContactLimits.FullNameMaxLength) })).toEqual({});
    expect(contactFormErrors({ ...VALID, fullName: 'a'.repeat(ContactLimits.FullNameMaxLength + 1) })[ContactFields.FullName]).toEqual({
      key: Keys.FullNameTooLong,
      params: { max: ContactLimits.FullNameMaxLength },
    });
  });

  it('checks the message bounds after trimming', () => {
    const short = 'a'.repeat(ContactLimits.MessageMinLength - 1);
    expect(contactFormErrors({ ...VALID, message: `  ${short}  ` })[ContactFields.Message]).toEqual({
      key: Keys.MessageTooShort,
      params: { min: ContactLimits.MessageMinLength },
    });
    expect(contactFormErrors({ ...VALID, message: 'a'.repeat(ContactLimits.MessageMinLength) })).toEqual({});
    expect(contactFormErrors({ ...VALID, message: 'a'.repeat(ContactLimits.MessageMaxLength) })).toEqual({});
    expect(contactFormErrors({ ...VALID, message: 'a'.repeat(ContactLimits.MessageMaxLength + 1) })[ContactFields.Message]).toEqual({
      key: Keys.MessageTooLong,
      params: { max: ContactLimits.MessageMaxLength },
    });
  });
});

describe('contactFieldOf', () => {
  it('maps the server field keys in either case and refuses others', () => {
    expect(contactFieldOf('fullName')).toBe(ContactFields.FullName);
    expect(contactFieldOf('FullName')).toBe(ContactFields.FullName);
    expect(contactFieldOf('TOPICID')).toBe(ContactFields.TopicId);
    expect(contactFieldOf('phone')).toBeNull();
  });
});

describe('CONTACT_TOPICS', () => {
  it('lists every topic id once, with a portal label key', () => {
    expect(CONTACT_TOPICS.map(topic => topic.id)).toEqual(Object.values(ContactTopicIds));
    for (const topic of CONTACT_TOPICS) {
      expect(topic.labelKey).toMatch(/^portal\.contact\.topic-/);
    }
  });
});
