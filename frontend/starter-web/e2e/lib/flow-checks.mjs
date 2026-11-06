// The pass rules and test data of the scripted flows (pure: page text and the run stamp in, verdicts and data out).
import {
  AppKeys,
  CONTACT_REFERENCE_MIN_DIGITS,
  CONTACT_REFERENCE_PREFIX,
  FlowData,
  IMPORT_HEADERS,
} from './flow-constants.mjs';

const NON_ALPHANUMERIC = /[^a-z0-9]/gi;
const STAMP_LENGTH = 12;
const NON_DIGITS = /\D/g;

/** The reference number body: the year (captured) and at least the minimum digits (captured). */
const REFERENCE_SOURCE = String.raw`${CONTACT_REFERENCE_PREFIX}-(\d{4})-(\d{${CONTACT_REFERENCE_MIN_DIGITS},})`;
const WORD_EDGE = String.raw`\b`;

/** Finds a reference number in page text (any year: contactReferenceFor checks the year). */
export const CONTACT_REFERENCE_PATTERN = new RegExp(REFERENCE_SOURCE);

/** The contact reference numbers in a text: CU-YYYY-NNN (the year as four digits, at least three digits after it). */
export function findContactReferences(text) {
  return [...String(text).matchAll(new RegExp(`${WORD_EDGE}${REFERENCE_SOURCE}${WORD_EDGE}`, 'g'))].map((match) => ({ value: match[0], year: Number(match[1]) }));
}

/** The reference number of the current year in a text, or null (a stale or malformed number does not count). */
export function contactReferenceFor(text, year) {
  return findContactReferences(text).find((reference) => reference.year === year)?.value ?? null;
}

/** A short lower-case token unique to this run (from the ISO run stamp), safe in user names and e-mail addresses. */
export function runToken(runStamp) {
  return runStamp.replace(NON_ALPHANUMERIC, '').slice(-STAMP_LENGTH).toLowerCase();
}

/** The imported user of this run: { userName, displayName, email }. */
export function importedUser(runStamp) {
  const token = runToken(runStamp);
  return {
    userName: `${FlowData.UserPrefix}${token}`,
    displayName: `${FlowData.DisplayNamePrefix}${token}`,
    email: `${FlowData.UserPrefix}${token}@${FlowData.EmailDomain}`,
  };
}

/** The import sheet rows (header row first) for one password-bound internal user. */
export function importRows(user) {
  return [[...IMPORT_HEADERS], [user.userName, user.displayName, user.email, AppKeys.ImportKindInternal, AppKeys.ImportBindingPassword]];
}

/** The password the activated user sets (unique per run; passes length and character-class rules). */
export function activationPassword(runStamp) {
  return `${FlowData.NewPasswordPrefix}${runToken(runStamp)}`;
}

/** The FAQ title marker of this run in each language. */
export function faqMarkers(runStamp) {
  const token = runToken(runStamp);
  return { ar: `${FlowData.FaqMarkerPrefix}${token} ${FlowData.FaqMarkerSuffixes.ar}`, en: `${FlowData.FaqMarkerPrefix}${token} ${FlowData.FaqMarkerSuffixes.en}` };
}

/** The whole-number setting value to write: the shown value plus the delta, parsed from digits only. */
export function nextSettingValue(shownText) {
  const digits = String(shownText).replace(NON_DIGITS, '');
  if (digits === '') return null;
  return String(Number(digits) + FlowData.SettingDelta);
}

/** The visitor's contact e-mail of this run (reserved .test domain). */
export function contactEmail(runStamp) {
  return `contact-${runToken(runStamp)}@${FlowData.EmailDomain}`;
}
