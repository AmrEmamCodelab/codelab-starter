import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { IdentityErrorCodes, LOGIN_TRANSLATED_CODES, LoginMessageKeys, ssoErrorRejection } from '../../core/auth/login-rejection';
import { ActivationMessageKeys } from './activate/activation-form';
import { LoginMethodTextKeys, LoginTextKeys } from './login-form';
import { SignInMessageKeys } from './sign-in.service';
import { SsoCallbackTextKeys } from './sso-callback/sso-callback-page.component';

const I18N_DIR = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', 'assets', 'i18n');

function flatten(tree: Record<string, unknown>, prefix = ''): Map<string, string> {
  const keys = new Map<string, string>();
  for (const [name, value] of Object.entries(tree)) {
    const key = prefix === '' ? name : `${prefix}.${name}`;
    if (typeof value === 'string') {
      keys.set(key, value);
    } else {
      flatten(value as Record<string, unknown>, key).forEach((text, child) => keys.set(child, text));
    }
  }
  return keys;
}

/** Every key the sign-in, SSO callback and activation pages render from code (the templates' literals included). */
const AUTH_KEYS: readonly string[] = [
  ...Object.values(LoginMessageKeys),
  ...Object.values(SignInMessageKeys),
  ...Object.values(LoginTextKeys),
  ...Object.values(LoginMethodTextKeys).flatMap(texts => [texts.tab, texts.subtitle]),
  ...Object.values(SsoCallbackTextKeys),
  ...Object.values(ActivationMessageKeys),
  ...[...LOGIN_TRANSLATED_CODES].map(code => `errors.${code}`),
  ...Object.values(IdentityErrorCodes).map(code => ssoErrorRejection(code).messageKey),
  'auth.login.title',
  'auth.login.user-name-label',
  'auth.login.password-label',
  'auth.login.submit',
  'auth.login.sso-submit',
];

describe('auth translations', () => {
  for (const language of ['ar', 'en']) {
    const bundle = flatten(JSON.parse(readFileSync(join(I18N_DIR, `${language}.json`), 'utf8')) as Record<string, unknown>);

    it(`has every sign-in key in ${language}.json`, () => {
      expect(AUTH_KEYS.filter(key => !bundle.has(key))).toEqual([]);
    });

    it(`keeps the {{min}} parameter of the password length messages in ${language}.json`, () => {
      expect(bundle.get(ActivationMessageKeys.PasswordTooShort)).toContain('{{min}}');
      expect(bundle.get(ActivationMessageKeys.PasswordHint)).toContain('{{min}}');
    });
  }
});
