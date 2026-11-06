import { ERROR_MESSAGE_KEY_PREFIX, type ApiError } from '@codelab/ng-security';
import { NO_FIELD_ERRORS, type FieldErrorMap } from '../../../patterns';

/*
 * The client half of the Service Request wizard validation. It is HAND-MIRRORED from the backend FluentValidation
 * validators of Modules/ServiceRequests (no metadata generator): the same limits, the same pattern, the same date
 * rule, the same step scopes. `validation-parity.fixtures.json` pins both sides: the FE parity spec and the backend
 * parity test run every case and must produce the same `valid` and `errorFields`. Change both sides and the fixture
 * together.
 *
 * Semantics both sides share:
 * - A text is missing when it is null, absent or only white space. Lengths count the value as sent (UTF-16 units).
 * - `categoryId` is a lookup PublicId token; only its presence is checked here (the server checks it names an active
 *   category, which no client can mirror).
 * - `preferredDate` is optional; when given it is a real calendar date `YYYY-MM-DD` and not before `today`, the date
 *   in the app time zone (`SERVICE_REQUEST_TIME_ZONE`).
 * - Each field reports at most one code: the first rule it breaks, in the order required, format, length, range.
 */

/** The limits of the wizard fields (backend `ServiceRequestLimits`). */
export const ServiceRequestLimits = {
  TitleMinLength: 5,
  TitleMaxLength: 200,
  DescriptionMinLength: 20,
  DescriptionMaxLength: 2000,
} as const;

/** A Saudi mobile number: `05` and 8 more digits (backend `ServiceRequestLimits.ContactPhonePattern`). */
export const CONTACT_PHONE_PATTERN = /^05[0-9]{8}$/;

/** The wire shape of a date-only value. */
const ISO_DATE_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;

/** The validated fields, by their camelCase wire names (the keys of the API's `errors` dictionary). */
export const ServiceRequestFields = {
  CategoryId: 'categoryId',
  Title: 'title',
  Description: 'description',
  ContactPhone: 'contactPhone',
  PreferredDate: 'preferredDate',
} as const;

export type ServiceRequestField = (typeof ServiceRequestFields)[keyof typeof ServiceRequestFields];

/** The validation codes, the same strings the backend validators send as field-error messages. */
export const ServiceRequestValidationCodes = {
  CategoryRequired: 'SERVICE_REQUESTS_CATEGORY_REQUIRED',
  TitleRequired: 'SERVICE_REQUESTS_TITLE_REQUIRED',
  TitleTooShort: 'SERVICE_REQUESTS_TITLE_TOO_SHORT',
  TitleTooLong: 'SERVICE_REQUESTS_TITLE_TOO_LONG',
  DescriptionRequired: 'SERVICE_REQUESTS_DESCRIPTION_REQUIRED',
  DescriptionTooShort: 'SERVICE_REQUESTS_DESCRIPTION_TOO_SHORT',
  DescriptionTooLong: 'SERVICE_REQUESTS_DESCRIPTION_TOO_LONG',
  ContactPhoneRequired: 'SERVICE_REQUESTS_CONTACT_PHONE_REQUIRED',
  ContactPhoneInvalid: 'SERVICE_REQUESTS_CONTACT_PHONE_INVALID',
  PreferredDateInvalid: 'SERVICE_REQUESTS_PREFERRED_DATE_INVALID',
  PreferredDateInPast: 'SERVICE_REQUESTS_PREFERRED_DATE_IN_PAST',
} as const;

export type ServiceRequestValidationCode =
  (typeof ServiceRequestValidationCodes)[keyof typeof ServiceRequestValidationCodes];

const KNOWN_CODES: ReadonlySet<string> = new Set(Object.values(ServiceRequestValidationCodes));

/** What one `validateServiceRequest` call checks: a wizard step (a draft save or Next) or the whole submit. */
export const ServiceRequestValidationScopes = {
  Step1: 'step1',
  Step2: 'step2',
  Submit: 'submit',
} as const;

export type ServiceRequestValidationScope =
  (typeof ServiceRequestValidationScopes)[keyof typeof ServiceRequestValidationScopes];

/** The fields each scope validates. Step 3 has no field of its own: its check is the server's attachment interlock. */
export const SERVICE_REQUEST_SCOPE_FIELDS: Readonly<Record<ServiceRequestValidationScope, readonly ServiceRequestField[]>> = {
  [ServiceRequestValidationScopes.Step1]: [ServiceRequestFields.CategoryId, ServiceRequestFields.Title],
  [ServiceRequestValidationScopes.Step2]: [
    ServiceRequestFields.Description,
    ServiceRequestFields.ContactPhone,
    ServiceRequestFields.PreferredDate,
  ],
  [ServiceRequestValidationScopes.Submit]: Object.values(ServiceRequestFields),
};

/** The input of a validation: any subset of the draft fields (an absent field counts as null). */
export interface ServiceRequestValidationInput {
  readonly categoryId?: string | null;
  readonly title?: string | null;
  readonly description?: string | null;
  readonly contactPhone?: string | null;
  readonly preferredDate?: string | null;
}

/** The outcome of a validation. */
export interface ServiceRequestValidationResult {
  readonly valid: boolean;
  /** The failing fields, sorted by name (ordinal): the value the parity fixture compares. */
  readonly errorFields: readonly ServiceRequestField[];
  /** The one code per failing field. */
  readonly errors: Readonly<Partial<Record<ServiceRequestField, ServiceRequestValidationCode>>>;
}

function isMissing(value: string | null | undefined): value is null | undefined {
  return value === null || value === undefined || value.trim() === '';
}

function lengthProblem(
  value: string,
  limits: { readonly min: number; readonly max: number },
  codes: { readonly tooShort: ServiceRequestValidationCode; readonly tooLong: ServiceRequestValidationCode },
): ServiceRequestValidationCode | null {
  if (value.length < limits.min) return codes.tooShort;
  return value.length > limits.max ? codes.tooLong : null;
}

/** The code `categoryId` breaks, or null. */
export function categoryIdProblem(value: string | null | undefined): ServiceRequestValidationCode | null {
  return isMissing(value) ? ServiceRequestValidationCodes.CategoryRequired : null;
}

/** The code `title` breaks, or null. */
export function titleProblem(value: string | null | undefined): ServiceRequestValidationCode | null {
  if (isMissing(value)) return ServiceRequestValidationCodes.TitleRequired;
  return lengthProblem(
    value,
    { min: ServiceRequestLimits.TitleMinLength, max: ServiceRequestLimits.TitleMaxLength },
    { tooShort: ServiceRequestValidationCodes.TitleTooShort, tooLong: ServiceRequestValidationCodes.TitleTooLong },
  );
}

/** The code `description` breaks, or null. */
export function descriptionProblem(value: string | null | undefined): ServiceRequestValidationCode | null {
  if (isMissing(value)) return ServiceRequestValidationCodes.DescriptionRequired;
  return lengthProblem(
    value,
    { min: ServiceRequestLimits.DescriptionMinLength, max: ServiceRequestLimits.DescriptionMaxLength },
    {
      tooShort: ServiceRequestValidationCodes.DescriptionTooShort,
      tooLong: ServiceRequestValidationCodes.DescriptionTooLong,
    },
  );
}

/** The code `contactPhone` breaks, or null. */
export function contactPhoneProblem(value: string | null | undefined): ServiceRequestValidationCode | null {
  if (isMissing(value)) return ServiceRequestValidationCodes.ContactPhoneRequired;
  return CONTACT_PHONE_PATTERN.test(value) ? null : ServiceRequestValidationCodes.ContactPhoneInvalid;
}

/** True when `value` is `YYYY-MM-DD` and names a real calendar date (no 2026-02-30). */
export function isIsoDate(value: string): boolean {
  const match = ISO_DATE_PATTERN.exec(value);
  if (match === null) return false;
  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const date = new Date(Date.UTC(year, month - 1, day));
  return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day;
}

/**
 * The code `preferredDate` breaks, or null. Optional: missing is valid. `today` is the `YYYY-MM-DD` date in the app
 * time zone (`todayInTimeZone`); ISO dates compare correctly as strings.
 */
export function preferredDateProblem(value: string | null | undefined, today: string): ServiceRequestValidationCode | null {
  if (isMissing(value)) return null;
  if (!isIsoDate(value)) return ServiceRequestValidationCodes.PreferredDateInvalid;
  return value < today ? ServiceRequestValidationCodes.PreferredDateInPast : null;
}

function fieldProblem(
  field: ServiceRequestField,
  input: ServiceRequestValidationInput,
  today: string,
): ServiceRequestValidationCode | null {
  switch (field) {
    case ServiceRequestFields.CategoryId:
      return categoryIdProblem(input.categoryId);
    case ServiceRequestFields.Title:
      return titleProblem(input.title);
    case ServiceRequestFields.Description:
      return descriptionProblem(input.description);
    case ServiceRequestFields.ContactPhone:
      return contactPhoneProblem(input.contactPhone);
    case ServiceRequestFields.PreferredDate:
      return preferredDateProblem(input.preferredDate, today);
  }
}

/**
 * Validates `input` for one scope, the same way the server does: `step1` (category, title), `step2` (description,
 * contact phone, preferred date) or `submit` (every field; a submit that skipped a step fails on that step's fields).
 * `today` is `YYYY-MM-DD` in the app time zone.
 */
export function validateServiceRequest(
  input: ServiceRequestValidationInput,
  scope: ServiceRequestValidationScope,
  today: string,
): ServiceRequestValidationResult {
  const errors: Partial<Record<ServiceRequestField, ServiceRequestValidationCode>> = {};
  for (const field of SERVICE_REQUEST_SCOPE_FIELDS[scope]) {
    const code = fieldProblem(field, input, today);
    if (code !== null) errors[field] = code;
  }
  const errorFields = (Object.keys(errors) as ServiceRequestField[]).sort(compareOrdinal);
  return { valid: errorFields.length === 0, errorFields, errors };
}

function compareOrdinal(left: string, right: string): number {
  if (left === right) return 0;
  return left < right ? -1 : 1;
}

/** The translation-key prefix of the sample's validation messages. */
export const SERVICE_REQUEST_ERROR_KEY_PREFIX = 'serviceRequests.errors.';

/**
 * The translation key for a validation message: `serviceRequests.errors.<CODE>` for a code of this module, the
 * platform's `errors.<CODE>` for any other (a server message this module does not own, such as `REQUIRED`).
 */
export function serviceRequestMessageKey(code: string): string {
  return KNOWN_CODES.has(code) ? `${SERVICE_REQUEST_ERROR_KEY_PREFIX}${code}` : `${ERROR_MESSAGE_KEY_PREFIX}${code}`;
}

/** A client result as the wizard pattern's field-error map (field to message keys). */
export function serviceRequestFieldErrors(result: ServiceRequestValidationResult): FieldErrorMap {
  if (result.valid) return NO_FIELD_ERRORS;
  const map: Record<string, readonly string[]> = {};
  for (const field of result.errorFields) {
    const code = result.errors[field];
    if (code !== undefined) map[field] = [serviceRequestMessageKey(code)];
  }
  return map;
}

/**
 * A refused save or submit (`parseApiError(...)`) as the wizard pattern's field-error map, with this module's codes
 * rendered from `serviceRequests.errors.*`. Duplicate messages for a field are kept once; null or an error without
 * field errors gives `NO_FIELD_ERRORS`.
 */
export function serviceRequestFieldErrorsFrom(error: ApiError | null | undefined): FieldErrorMap {
  if (!error || error.fieldErrors.length === 0) return NO_FIELD_ERRORS;
  const grouped: Record<string, string[]> = {};
  for (const { field, message } of error.fieldErrors) {
    const keys = (grouped[field] ??= []);
    const key = message.startsWith(ERROR_MESSAGE_KEY_PREFIX) ? message : serviceRequestMessageKey(message);
    if (!keys.includes(key)) keys.push(key);
  }
  return grouped;
}
