import { ApiErrorCodes, ERROR_MESSAGE_KEY_PREFIX } from '@codelab/ng-security';

/** Translation keys shared by the shell, the list pattern and every screen (`src/assets/i18n/<lang>.json`). */
export const CommonKeys = {
  AppName: 'app.name',
  Nav: {
    Label: 'nav.label',
    Menu: 'nav.menu',
    Overview: 'nav.overview',
    Reference: 'nav.reference',
    Jobs: 'nav.jobs',
    Certificates: 'nav.certificates',
  },
  Language: {
    /** The toggle's label, in the language it switches TO. */
    Switch: 'common.language.switch',
  },
  Operator: {
    SignedInAs: 'common.operator.signedInAs',
  },
  Actions: {
    Refresh: 'common.actions.refresh',
    Retry: 'common.actions.retry',
  },
  List: {
    Loading: 'common.list.loading',
    Empty: 'common.list.empty',
    Error: 'common.list.error',
  },
  /** `{{code}}`: the reference code of a failed call, for the helpdesk and the reference lookup. */
  ReferenceCode: 'common.referenceCode',
  NotAvailable: 'common.notAvailable',
} as const;

/** The `errors.<CODE>` keys of every platform error code (the messages `apiErrorMessageKey` points at). */
export const ERROR_MESSAGE_KEYS: readonly string[] = Object.freeze(
  Object.values(ApiErrorCodes).map((code) => `${ERROR_MESSAGE_KEY_PREFIX}${code}`),
);
