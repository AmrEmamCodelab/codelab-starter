// "Arabic present in ar": every Arabic translation that contains words has at least one Arabic letter. Placeholders
// ({{name}}), web addresses and e-mail addresses are not words; a text with no letters left ("{{count}}", "-") passes.
// A key whose Arabic text is deliberately Latin (the name of the English language in a language switcher) is listed
// in `allowLatin`: an exact key, or a group written as `prefix.*`.
import { I18nProblemKinds } from './translations.mjs';

export const ARABIC_LANGUAGE = 'ar';
const PLACEHOLDER = /\{\{[^}]*\}\}/g;
const WEB_ADDRESS = /\bhttps?:\/\/\S+/g;
const EMAIL_ADDRESS = /[\w.+-]+@[\w-]+(?:\.[\w-]+)+/g;
const ANY_LETTER = /\p{L}/u;
const ARABIC_LETTER = /\p{Script=Arabic}/u;
const GROUP_WILDCARD = '.*';
const SEPARATOR = '.';

/** True when `text` has words but no Arabic letter. */
export function lacksArabic(text) {
  const words = text.replace(PLACEHOLDER, '').replace(WEB_ADDRESS, '').replace(EMAIL_ADDRESS, '');
  return ANY_LETTER.test(words) && !ARABIC_LETTER.test(words);
}

/** True when `key` is listed in `allowLatin` (exactly, or under a `prefix.*` entry). */
export function isLatinAllowed(key, allowLatin) {
  return allowLatin.some((entry) => (entry.endsWith(GROUP_WILDCARD)
    ? key.startsWith(entry.slice(0, -GROUP_WILDCARD.length) + SEPARATOR)
    : key === entry));
}

/** One missing-arabic problem per Arabic leaf with words but no Arabic letter. `leaves` is the flattened `ar` file. */
export function findLeavesWithoutArabic(leaves, allowLatin = []) {
  return [...leaves].filter(([key, text]) => lacksArabic(text) && !isLatinAllowed(key, allowLatin)).map(([key, text]) => ({
    kind: I18nProblemKinds.missingArabic,
    language: ARABIC_LANGUAGE,
    key,
    message: `${ARABIC_LANGUAGE}: ${key} has no Arabic text: "${text}"`,
  }));
}
