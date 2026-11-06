/**
 * Every refusal code the attachment flow can surface: the attachment service's (contract section 1) and the API
 * Attachments module's (section 3). Each has an `errors.<CODE>` key in both translation bundles. Platform codes
 * (`ApiErrorCodes` of `@codelab/ng-security`, such as `INTERNAL_ERROR` or `NETWORK`) can surface too.
 */
export const AttachmentErrorCodes = {
  // The attachment service.
  TicketInvalid: 'ATTACHMENTS_TICKET_INVALID',
  FileTooLarge: 'ATTACHMENTS_FILE_TOO_LARGE',
  TooManyFiles: 'ATTACHMENTS_TOO_MANY_FILES',
  TypeNotAllowed: 'ATTACHMENTS_TYPE_NOT_ALLOWED',
  SignatureMismatch: 'ATTACHMENTS_SIGNATURE_MISMATCH',
  EmptyFile: 'ATTACHMENTS_EMPTY_FILE',
  FileNotFound: 'ATTACHMENTS_FILE_NOT_FOUND',
  // The API Attachments module.
  ReceiptInvalid: 'ATTACHMENTS_RECEIPT_INVALID',
  RecordTypeUnknown: 'ATTACHMENTS_RECORD_TYPE_UNKNOWN',
  TypeNotFound: 'ATTACHMENTS_TYPE_NOT_FOUND',
  ExtensionNotAllowedForType: 'ATTACHMENTS_EXTENSION_NOT_ALLOWED_FOR_TYPE',
  MaxFilesReached: 'ATTACHMENTS_MAX_FILES_REACHED',
  NotFound: 'ATTACHMENTS_NOT_FOUND',
  RequiredMissing: 'ATTACHMENTS_REQUIRED_MISSING',
  StorageUnavailable: 'ATTACHMENTS_STORAGE_UNAVAILABLE',
} as const;

export type AttachmentErrorCode = (typeof AttachmentErrorCodes)[keyof typeof AttachmentErrorCodes];

const KNOWN_CODES: ReadonlySet<string> = new Set(Object.values(AttachmentErrorCodes));

/** Whether `code` is one of the attachment flow's own codes (not a platform code). */
export function isAttachmentErrorCode(code: string): code is AttachmentErrorCode {
  return KNOWN_CODES.has(code);
}
