/** The languages the console ships. Arabic is the default and renders right-to-left. */
export const AppLanguages = {
  Arabic: 'ar',
  English: 'en',
} as const;

export type AppLanguage = (typeof AppLanguages)[keyof typeof AppLanguages];

export const DEFAULT_LANGUAGE: AppLanguage = AppLanguages.Arabic;

export const TextDirections = {
  RightToLeft: 'rtl',
  LeftToRight: 'ltr',
} as const;

export type TextDirection = (typeof TextDirections)[keyof typeof TextDirections];

const RIGHT_TO_LEFT_LANGUAGES: ReadonlySet<AppLanguage> = new Set([AppLanguages.Arabic]);
const SUPPORTED_LANGUAGES: ReadonlySet<string> = new Set(Object.values(AppLanguages));

export function isAppLanguage(value: unknown): value is AppLanguage {
  return typeof value === 'string' && SUPPORTED_LANGUAGES.has(value);
}

export function directionOf(language: AppLanguage): TextDirection {
  return RIGHT_TO_LEFT_LANGUAGES.has(language) ? TextDirections.RightToLeft : TextDirections.LeftToRight;
}

/** The other shipped language: what the language toggle switches to. */
export function otherLanguage(language: AppLanguage): AppLanguage {
  return language === AppLanguages.Arabic ? AppLanguages.English : AppLanguages.Arabic;
}
