import { describe, expect, it } from 'vitest';
import { CoreTranslationKeys } from '../../core/i18n/core-translation-keys';
import { SessionEndReasons } from '../../core/routing/route-paths';
import { LoginFieldMessageKeys, credentialErrors, hasCredentialErrors, sessionEndedNoticeKey } from './login-form';

describe('credentialErrors', () => {
  it('accepts a user name and a password', () => {
    const errors = credentialErrors({ userName: 'admin', password: 'secret' });
    expect(errors).toEqual({ userName: null, password: null });
    expect(hasCredentialErrors(errors)).toBe(false);
  });

  it('requires both fields', () => {
    const errors = credentialErrors({ userName: '', password: '' });
    expect(errors).toEqual({
      userName: LoginFieldMessageKeys.UserNameRequired,
      password: LoginFieldMessageKeys.PasswordRequired,
    });
    expect(hasCredentialErrors(errors)).toBe(true);
  });

  it('treats a blank user name as empty but keeps a password of spaces', () => {
    expect(credentialErrors({ userName: '   ', password: '  ' })).toEqual({
      userName: LoginFieldMessageKeys.UserNameRequired,
      password: null,
    });
  });
});

describe('sessionEndedNoticeKey', () => {
  it('maps each session end reason to its core notice', () => {
    for (const reason of Object.values(SessionEndReasons)) {
      expect(sessionEndedNoticeKey(reason)).toBe(CoreTranslationKeys.SessionEnded[reason]);
    }
  });

  it('shows nothing for no reason or an unknown one', () => {
    expect(sessionEndedNoticeKey(null)).toBeNull();
    expect(sessionEndedNoticeKey(undefined)).toBeNull();
    expect(sessionEndedNoticeKey('toString')).toBeNull();
  });
});
