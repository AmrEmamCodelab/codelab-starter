import { ERROR_MESSAGE_KEY_PREFIX, type ApiError } from '@codelab/ng-security';

/**
 * Field errors as the editor dialog and its fields use them: the camelCase field path (as the API's problem+json
 * `errors` dictionary names it) to the translation keys of its messages, in server order.
 */
export type FieldErrorMap = Readonly<Record<string, readonly string[]>>;

/** No field errors. */
export const NO_FIELD_ERRORS: FieldErrorMap = Object.freeze({});

/**
 * The translation key for one server message. The starter's validators send message CODES (`USER_NAME_REQUIRED`),
 * which render as `errors.<CODE>`; a message that already is a key (`errors.X`) is kept.
 */
export function fieldMessageKey(message: string): string {
  return message.startsWith(ERROR_MESSAGE_KEY_PREFIX) ? message : `${ERROR_MESSAGE_KEY_PREFIX}${message}`;
}

/**
 * The field-error map of a failed save (`parseApiError(...).fieldErrors`), grouped by field. Duplicate messages for a
 * field are kept once. Null or an error without field errors gives {@link NO_FIELD_ERRORS}.
 */
export function fieldErrorsFrom(error: ApiError | null | undefined): FieldErrorMap {
  if (!error || error.fieldErrors.length === 0) {
    return NO_FIELD_ERRORS;
  }
  const grouped: Record<string, string[]> = {};
  for (const { field, message } of error.fieldErrors) {
    const keys = (grouped[field] ??= []);
    const key = fieldMessageKey(message);
    if (!keys.includes(key)) {
      keys.push(key);
    }
  }
  return grouped;
}

/** The first message key for `field` (what a single-error control such as `dga-text-input` shows), or null. */
export function firstFieldError(errors: FieldErrorMap, field: string): string | null {
  return errors[field]?.[0] ?? null;
}

/** Every message key of the map, flattened in field order, for the dialog's error summary. */
export function allFieldErrors(errors: FieldErrorMap): readonly { readonly field: string; readonly key: string }[] {
  return Object.entries(errors).flatMap(([field, keys]) => keys.map((key) => ({ field, key })));
}

/** True when the map holds at least one message. */
export function hasFieldErrors(errors: FieldErrorMap): boolean {
  return Object.values(errors).some((keys) => keys.length > 0);
}
