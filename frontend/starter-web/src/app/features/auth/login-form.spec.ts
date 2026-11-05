import { describe, expect, it } from 'vitest';
import { CoreTranslationKeys } from '../../core/i18n/core-translation-keys';
import { SignInMode } from '../../core/auth/sign-in-mode';
import { LoginNotices, SessionEndReasons } from '../../core/routing/route-paths';
import {
  LoginFieldMessageKeys,
  LoginTextKeys,
  credentialErrors,
  hasCredentialErrors,
  loginMethodsFrom,
  loginNoticeKey,
  sessionEndedNoticeKey,
  usesPasswordForm,
} from './login-form';

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

describe('loginMethodsFrom', () => {
  it.each([
    [{ basic: true, activeDirectory: false, sso: false }, [SignInMode.Basic]],
    [{ basic: false, activeDirectory: true, sso: false }, [SignInMode.ActiveDirectory]],
    [{ basic: false, activeDirectory: false, sso: true }, [SignInMode.Sso]],
    [{ basic: true, activeDirectory: true, sso: false }, [SignInMode.Basic, SignInMode.ActiveDirectory]],
    [{ basic: true, activeDirectory: false, sso: true }, [SignInMode.Basic, SignInMode.Sso]],
    [{ basic: false, activeDirectory: true, sso: true }, [SignInMode.ActiveDirectory, SignInMode.Sso]],
    [{ basic: true, activeDirectory: true, sso: true }, [SignInMode.Basic, SignInMode.ActiveDirectory, SignInMode.Sso]],
    [{ basic: false, activeDirectory: false, sso: false }, []],
  ])('lists the enabled methods of %j in a fixed order', (modes, expected) => {
    expect(loginMethodsFrom(modes)).toEqual(expected);
  });

  it('treats anything but true as disabled', () => {
    expect(loginMethodsFrom({ basic: 'yes', activeDirectory: 1, sso: null } as never)).toEqual([]);
  });
});

describe('usesPasswordForm', () => {
  it('is true for basic and Active Directory, false for SSO', () => {
    expect(usesPasswordForm(SignInMode.Basic)).toBe(true);
    expect(usesPasswordForm(SignInMode.ActiveDirectory)).toBe(true);
    expect(usesPasswordForm(SignInMode.Sso)).toBe(false);
  });
});

describe('loginNoticeKey', () => {
  it('shows the activation notice', () => {
    expect(loginNoticeKey(LoginNotices.Activated)).toBe(LoginTextKeys.ActivatedNotice);
  });

  it('shows nothing for no notice or an unknown one', () => {
    expect(loginNoticeKey(null)).toBeNull();
    expect(loginNoticeKey('other')).toBeNull();
  });
});
