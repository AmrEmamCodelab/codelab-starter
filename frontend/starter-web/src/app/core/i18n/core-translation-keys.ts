import { ApiErrorCodes, ERROR_MESSAGE_KEY_PREFIX } from '@codelab/ng-security';
import { SessionEndReasons } from '../routing/route-paths';

/**
 * Every translation key core code produces or expects the app to render. fe-features adds each one to
 * both `src/assets/i18n/ar.json` and `en.json`; the i18n parity check and this module's spec keep the
 * list honest.
 */
export const CoreTranslationKeys = {
  /** One message per platform error code, `errors.<CODE>` (what `ErrorDialogStore.messageKey` returns). */
  Errors: Object.fromEntries(
    Object.values(ApiErrorCodes).map(code => [code, `${ERROR_MESSAGE_KEY_PREFIX}${code}`]),
  ) as Readonly<Record<(typeof ApiErrorCodes)[keyof typeof ApiErrorCodes], string>>,
  /** The error dialog the layout renders from `ErrorDialogStore`. */
  ErrorDialog: {
    Title: 'core.error-dialog.title',
    ReferenceCode: 'core.error-dialog.reference-code',
    Dismiss: 'core.error-dialog.dismiss',
  },
  /** The notice the login page shows for `?reason=<SessionEndReason>`. */
  SessionEnded: {
    [SessionEndReasons.Expired]: 'core.session-ended.expired',
    [SessionEndReasons.Idle]: 'core.session-ended.idle',
  },
} as const;

/** The flat list of every core key. */
export const CORE_TRANSLATION_KEYS: readonly string[] = [
  ...Object.values(CoreTranslationKeys.Errors),
  ...Object.values(CoreTranslationKeys.ErrorDialog),
  ...Object.values(CoreTranslationKeys.SessionEnded),
];
