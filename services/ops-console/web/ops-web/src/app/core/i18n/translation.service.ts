import { Injectable, computed, inject, signal } from '@angular/core';
import { STATIC_JSON_FETCH } from '../http/static-json.loader';
import { DEFAULT_LANGUAGE, type AppLanguage } from './language';

/** Where the translation bundles are served (`src/assets/i18n/<lang>.json`), relative to the base href. */
export const TRANSLATIONS_PATH = 'assets/i18n';

const KEY_SEPARATOR = '.';
const PLACEHOLDER = /\{\{\s*([\w.-]+)\s*\}\}/g;

/** A nested translation bundle as stored in the JSON files. */
export interface TranslationTree {
  readonly [key: string]: string | TranslationTree;
}

export type TranslationParams = Readonly<Record<string, string | number>>;
type Dictionary = Readonly<Record<string, string>>;

/** Flattens a nested bundle into dotted keys (`{ a: { b: 'x' } }` becomes `a.b`). */
export function flattenTranslations(tree: TranslationTree, prefix = ''): Record<string, string> {
  const flat: Record<string, string> = {};
  for (const [name, value] of Object.entries(tree)) {
    const key = prefix === '' ? name : `${prefix}${KEY_SEPARATOR}${name}`;
    if (typeof value === 'string') {
      flat[key] = value;
    } else if (typeof value === 'object' && value !== null) {
      Object.assign(flat, flattenTranslations(value, key));
    }
  }
  return flat;
}

/** Replaces `{{name}}` placeholders; an unknown placeholder is left as written. */
export function interpolate(text: string, params?: TranslationParams): string {
  if (!params) {
    return text;
  }
  return text.replace(PLACEHOLDER, (placeholder, name: string) => (name in params ? String(params[name]) : placeholder));
}

/**
 * The console's translation catalogue (no third-party i18n library): one flat dictionary per language, loaded on first
 * use. A missing key renders as the key itself, so a gap is visible rather than blank.
 */
@Injectable({ providedIn: 'root' })
export class TranslationService {
  private readonly fetchJson = inject(STATIC_JSON_FETCH);
  private readonly dictionaries = signal<ReadonlyMap<AppLanguage, Dictionary>>(new Map());
  private readonly active = signal<AppLanguage>(DEFAULT_LANGUAGE);
  private readonly loading = new Map<AppLanguage, Promise<void>>();
  private readonly dictionary = computed(() => this.dictionaries().get(this.active()) ?? {});

  readonly language = this.active.asReadonly();

  /** Switches to `language`, loading its bundle first if needed. A bundle that fails to load leaves keys showing. */
  async use(language: AppLanguage): Promise<void> {
    await this.ensureBundle(language);
    this.active.set(language);
  }

  translate(key: string, params?: TranslationParams): string {
    return interpolate(this.dictionary()[key] ?? key, params);
  }

  private ensureBundle(language: AppLanguage): Promise<void> {
    let pending = this.loading.get(language);
    if (!pending) {
      pending = this.loadBundle(language);
      this.loading.set(language, pending);
    }
    return pending;
  }

  private async loadBundle(language: AppLanguage): Promise<void> {
    try {
      const bundle = (await this.fetchJson(`${TRANSLATIONS_PATH}/${language}.json`)) as TranslationTree;
      const next = new Map(this.dictionaries());
      next.set(language, flattenTranslations(bundle));
      this.dictionaries.set(next);
    } catch {
      // Keys render as themselves (visible, never blank); a later switch retries the download.
      this.loading.delete(language);
    }
  }
}
