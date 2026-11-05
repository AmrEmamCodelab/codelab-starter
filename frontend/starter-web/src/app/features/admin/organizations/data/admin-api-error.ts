import { HttpErrorResponse } from '@angular/common/http';
import {
  ApiErrorCodes,
  ERROR_MESSAGE_KEY_PREFIX,
  HttpStatus,
  apiErrorMessageKey,
  isResponseSignatureError,
  parseApiError,
  type ApiError,
} from '@codelab/ng-security';

/**
 * The problem+json refusal of a failed admin API call, parsed by `@codelab/ng-security`. An answer whose signature
 * could not be verified becomes a refusal with no server detail (its body was never delivered): `FORBIDDEN` when it
 * carried 403, so a revoked user sees the access error, else `INTERNAL_ERROR`. Anything else is a programming error
 * and is rethrown, never turned into a message. Shared by the users, organizations and import screens.
 */
export function apiErrorFrom(error: unknown): ApiError {
  if (error instanceof HttpErrorResponse) {
    return parseApiError(error);
  }
  if (isResponseSignatureError(error)) {
    const forbidden = error.status === HttpStatus.forbidden;
    return { status: error.status, code: forbidden ? ApiErrorCodes.forbidden : ApiErrorCodes.internalError, fieldErrors: [] };
  }
  throw error;
}

/** The translation key of an error code (`errors.<CODE>`), such as an import row's reason. */
export function errorCodeKey(code: string): string {
  return `${ERROR_MESSAGE_KEY_PREFIX}${code}`;
}

/** The Users module's refusals the admin screens render, as the backend `UsersErrorCodes`. */
export const UsersErrorCodes = {
  UserNotFound: 'USERS_USER_NOT_FOUND',
  OrganizationNotFound: 'USERS_ORGANIZATION_NOT_FOUND',
  OrganizationInactive: 'USERS_ORGANIZATION_INACTIVE',
  OrganizationKindMismatch: 'USERS_ORGANIZATION_KIND_MISMATCH',
  OrganizationCodeTaken: 'USERS_ORGANIZATION_CODE_TAKEN',
  OrganizationKindInUse: 'USERS_ORGANIZATION_KIND_IN_USE',
  BreakGlassProtected: 'USERS_BREAK_GLASS_PROTECTED',
  CannotDeactivateSelf: 'USERS_CANNOT_DEACTIVATE_SELF',
  ActivationLinkUnconfigured: 'USERS_ACTIVATION_LINK_UNCONFIGURED',
  ActivationNotApplicable: 'USERS_ACTIVATION_NOT_APPLICABLE',
  OrganizationOutOfScope: 'USERS_ORGANIZATION_OUT_OF_SCOPE',
  TargetOutranksCaller: 'USERS_TARGET_OUTRANKS_CALLER',
  CannotChangeOwnAccess: 'USERS_CANNOT_CHANGE_OWN_ACCESS',
  LastAccessManager: 'USERS_LAST_ACCESS_MANAGER',
  ImportFileRequired: 'USERS_IMPORT_FILE_REQUIRED',
  ImportFileInvalid: 'USERS_IMPORT_FILE_INVALID',
  ImportFileTooLarge: 'USERS_IMPORT_FILE_TOO_LARGE',
  ImportTooManyRows: 'USERS_IMPORT_TOO_MANY_ROWS',
  ImportNoRows: 'USERS_IMPORT_NO_ROWS',
  ImportColumnsMissing: 'USERS_IMPORT_COLUMNS_MISSING',
  ImportBatchNotFound: 'USERS_IMPORT_BATCH_NOT_FOUND',
  ImportBatchExpired: 'USERS_IMPORT_BATCH_EXPIRED',
  ImportBatchCommitted: 'USERS_IMPORT_BATCH_COMMITTED',
} as const;

/** The reasons of an import row (`errors` of a preview row), as the backend `UsersImportRowErrorCodes`. */
export const UsersImportRowErrorCodes = {
  UserNameRequired: 'USERS_IMPORT_ROW_USER_NAME_REQUIRED',
  UserNameTooLong: 'USERS_IMPORT_ROW_USER_NAME_TOO_LONG',
  UserNameDuplicate: 'USERS_IMPORT_ROW_USER_NAME_DUPLICATE',
  UserNameTaken: 'USERS_IMPORT_ROW_USER_NAME_TAKEN',
  DisplayNameRequired: 'USERS_IMPORT_ROW_DISPLAY_NAME_REQUIRED',
  DisplayNameTooLong: 'USERS_IMPORT_ROW_DISPLAY_NAME_TOO_LONG',
  EmailInvalid: 'USERS_IMPORT_ROW_EMAIL_INVALID',
  EmailDuplicate: 'USERS_IMPORT_ROW_EMAIL_DUPLICATE',
  DirectoryNameTooLong: 'USERS_IMPORT_ROW_DIRECTORY_NAME_TOO_LONG',
  KindInvalid: 'USERS_IMPORT_ROW_KIND_INVALID',
  BindingInvalid: 'USERS_IMPORT_ROW_BINDING_INVALID',
  OrganizationRequired: 'USERS_IMPORT_ROW_ORGANIZATION_REQUIRED',
  OrganizationNotFound: 'USERS_IMPORT_ROW_ORGANIZATION_NOT_FOUND',
  OrganizationInactive: 'USERS_IMPORT_ROW_ORGANIZATION_INACTIVE',
  OrganizationOutOfScope: 'USERS_IMPORT_ROW_ORGANIZATION_OUT_OF_SCOPE',
  OrganizationKindMismatch: 'USERS_IMPORT_ROW_ORGANIZATION_KIND_MISMATCH',
} as const;

/** Identity's account refusals that the Users endpoints pass through unchanged, as the backend `IdentityErrorCodes`. */
export const IdentityAccountErrorCodes = {
  AccountNotFound: 'IDENTITY_ACCOUNT_NOT_FOUND',
  UserNameTaken: 'IDENTITY_USER_NAME_TAKEN',
  EmailTaken: 'IDENTITY_EMAIL_TAKEN',
  LoginNameTaken: 'IDENTITY_LOGIN_NAME_TAKEN',
  UpnTaken: 'IDENTITY_UPN_TAKEN',
  BreakGlassProtected: 'IDENTITY_BREAK_GLASS_PROTECTED',
  ActivationNotApplicable: 'IDENTITY_ACTIVATION_NOT_APPLICABLE',
  AccountInactive: 'IDENTITY_ACCOUNT_INACTIVE',
  ActivationUnavailable: 'IDENTITY_ACTIVATION_UNAVAILABLE',
} as const;

/** The field-error message codes of the kernel validators (`ValidationMessages`), rendered as `errors.<CODE>`. */
export const ValidationMessageCodes = {
  Required: 'REQUIRED',
  TooLong: 'TOO_LONG',
  InvalidFormat: 'INVALID_FORMAT',
} as const;

/** Every code the admin screens have `errors.<CODE>` text for (the platform codes plus the ones above). */
export const ADMIN_TRANSLATED_CODES: ReadonlySet<string> = new Set([
  ...Object.values(ApiErrorCodes),
  ...Object.values(UsersErrorCodes),
  ...Object.values(IdentityAccountErrorCodes),
]);

/** The message key of a refusal: its own `errors.<CODE>` when the admin bundles translate it, else the generic one. */
export function adminErrorMessageKey(error: ApiError): string {
  return apiErrorMessageKey(error, ADMIN_TRANSLATED_CODES);
}
