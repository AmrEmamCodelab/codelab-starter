import { Injectable, computed, inject, signal } from '@angular/core';
import type { TranslationTree } from '../api/clients/api.models';
import { STATIC_JSON_FETCH } from '../http/static-json.loader';
import { DEFAULT_LANGUAGE, type AppLanguage } from './language';

/** Where the public translation bundles are served (`src/assets/i18n/<lang>.json`), relative to the base href. */
export const PUBLIC_TRANSLATIONS_PATH = 'assets/i18n';

const KEY_SEPARATOR = '.';
const PLACEHOLDER = /\{\{\s*([\w.-]+)\s*\}\}/g;

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
  return text.replace(PLACEHOLDER, (placeholder, name: string) =>
    name in params ? String(params[name]) : placeholder);
}

/**
 * The app's own translation catalogue (no third-party i18n library). Holds one flat dictionary per
 * language, loads the public bundle on first use, and merges further bundles (the session-gated half)
 * over it. A missing key renders as the key itself, so a gap is visible rather than blank.
 */
@Injectable({ providedIn: 'root' })
export class TranslationService {
  private readonly fetchJson = inject(STATIC_JSON_FETCH);
  private readonly dictionaries = signal<ReadonlyMap<AppLanguage, Dictionary>>(new Map());
  private readonly active = signal<AppLanguage>(DEFAULT_LANGUAGE);
  private readonly loading = new Map<AppLanguage, Promise<void>>();

  readonly language = this.active.asReadonly();
  private readonly dictionary = computed(() => this.dictionaries().get(this.active()) ?? {});

  /**
   * Switches to `language`, loading its public bundle first if needed. A bundle that fails to load
   * leaves the keys showing; it never blocks the switch.
   */
  async use(language: AppLanguage): Promise<void> {
    await this.ensurePublicBundle(language);
    this.active.set(language);
  }

  translate(key: string, params?: TranslationParams): string {
    return interpolate(this.dictionary()[key] ?? key, params);
  }

  has(key: string): boolean {
    return key in this.dictionary();
  }

  /** Merges `tree` over what `language` already holds (later keys win). */
  merge(language: AppLanguage, tree: TranslationTree): void {
    const next = new Map(this.dictionaries());
    next.set(language, { ...(next.get(language) ?? {}), ...flattenTranslations(tree) });
    this.dictionaries.set(next);
  }

  private ensurePublicBundle(language: AppLanguage): Promise<void> {
    let pending = this.loading.get(language);
    if (!pending) {
      pending = this.loadPublicBundle(language);
      this.loading.set(language, pending);
    }
    return pending;
  }

  private async loadPublicBundle(language: AppLanguage): Promise<void> {
    try {
      const bundle = await this.fetchJson(`${PUBLIC_TRANSLATIONS_PATH}/${language}.json`);
      this.merge(language, bundle as TranslationTree);
    } catch {
      // Keys render as themselves; allow a later switch to retry.
      this.loading.delete(language);
    }
  }
}
