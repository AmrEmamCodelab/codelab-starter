// The normalized view of a line that term rules also match against, so a term hidden inside an identifier or
// written with Arabic diacritics is still found:
//   - identifiers are split on case and digit boundaries and on '_': OrderId -> "Order Id",
//     IApiClient -> "I Api Client", MAX_RETRY_COUNT -> "MAX RETRY COUNT", isSubmitted -> "is Submitted";
//   - Arabic is normalized: tashkeel and tatweel removed, alef forms unified (أ إ آ ٱ -> ا), alef maqsura -> ya.
// Every character of the normalized text maps back to its index in the original line (for the finding column).

const SPACE = ' ';
const UNDERSCORE = '_';
const ARABIC_DIACRITICS = /[\u064B-\u065F\u0670\u06D6-\u06ED\u0640]/;
const ARABIC_ALEF_FORMS = Object.freeze({ '\u0623': '\u0627', '\u0625': '\u0627', '\u0622': '\u0627', '\u0671': '\u0627' });
const ARABIC_ALEF_MAQSURA = '\u0649';
const ARABIC_YA = '\u064A';
const LOWER = /\p{Ll}/u;
const UPPER = /\p{Lu}/u;
const DIGIT = /\d/;
const LETTER = /\p{L}/u;

function isCaseBoundary(previous, current, next) {
  if (previous === undefined) return false;
  if (LOWER.test(previous) && UPPER.test(current)) return true;
  if (UPPER.test(previous) && UPPER.test(current) && next !== undefined && LOWER.test(next)) return true;
  const letterThenDigit = LETTER.test(previous) && DIGIT.test(current);
  const digitThenLetter = DIGIT.test(previous) && LETTER.test(current);
  return letterThenDigit || digitThenLetter;
}

function normalizeArabic(char) {
  if (ARABIC_ALEF_FORMS[char]) return ARABIC_ALEF_FORMS[char];
  return char === ARABIC_ALEF_MAQSURA ? ARABIC_YA : char;
}

/**
 * @param {string} line
 * @returns {{ text: string, originalIndex: number[] }} the normalized text and, per character, its source index
 */
export function normalizeForTerms(line) {
  let text = '';
  const originalIndex = [];
  const chars = [...line];
  let offset = 0;
  for (let index = 0; index < chars.length; index += 1) {
    const char = chars[index];
    const position = offset;
    offset += char.length;
    if (ARABIC_DIACRITICS.test(char)) continue;
    if (isCaseBoundary(chars[index - 1], char, chars[index + 1])) { text += SPACE; originalIndex.push(position); }
    const normalized = char === UNDERSCORE ? SPACE : normalizeArabic(char);
    text += normalized;
    for (let unit = 0; unit < normalized.length; unit += 1) originalIndex.push(position);
  }
  return { text, originalIndex };
}
