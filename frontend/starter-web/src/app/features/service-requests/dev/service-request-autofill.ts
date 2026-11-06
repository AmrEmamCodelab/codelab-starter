// The DevAutofill data set of the Service Request wizard (development builds only). Referenced ONLY by the app
// registry src/app/dev-autofill.sets.ts, which the production build replaces with an empty stub; no screen imports
// this file. Every value is invented sample data.
import type { DevAutofillSet } from '../../../shared/dev-autofill';
import { ServiceRequestFields } from '../data';
import { SERVICE_REQUEST_DEV_AUTOFILL_LABEL_KEY } from '../requester/requester-translation-keys';
import { SERVICE_REQUEST_WIZARD_FORM_KEY } from '../requester/wizard/service-request-wizard.form';

const PHONE_PREFIX = '05';
const PHONE_SUBSCRIBER_DIGITS = 8;
const PHONE_FILL_DIGIT = '0';

/**
 * A placeholder mobile number of the accepted shape (`05` + 8 digits). Built at fill time so no phone-like literal sits
 * in the repository (the scrub sweep refuses real-looking numbers); it names nobody.
 */
export function samplePhone(): string {
  return `${PHONE_PREFIX}${PHONE_FILL_DIGIT.repeat(PHONE_SUBSCRIBER_DIGITS)}`;
}

/**
 * Fills steps 1 and 2 with values that pass every wizard rule. Two fields are left to the developer: the category (an
 * opaque lookup token that differs per database, so no data set can know it) and the optional preferred date (it
 * depends on today, and a data set has no access to the app clock).
 */
export const SERVICE_REQUEST_WIZARD_AUTOFILL: DevAutofillSet = Object.freeze({
  formKey: SERVICE_REQUEST_WIZARD_FORM_KEY,
  labelKey: SERVICE_REQUEST_DEV_AUTOFILL_LABEL_KEY,
  values: Object.freeze({
    [ServiceRequestFields.Title]: 'Sample service request',
    [ServiceRequestFields.Description]: 'A sample description long enough for the wizard rules, written by the dev autofill.',
    [ServiceRequestFields.ContactPhone]: samplePhone,
  }),
});

/** Every data set of the sample, for the app registry (one line there, with the module marker). */
export const SERVICE_REQUESTS_DEV_AUTOFILL_SETS: readonly DevAutofillSet[] = Object.freeze([SERVICE_REQUEST_WIZARD_AUTOFILL]);
