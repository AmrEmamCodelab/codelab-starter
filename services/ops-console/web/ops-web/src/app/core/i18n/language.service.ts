import { DOCUMENT, Injectable, computed, inject, signal } from '@angular/core';
import { OPS_CONFIG } from '../config/ops-config';
import { directionOf, isAppLanguage, otherLanguage, type AppLanguage } from './language';
import { TranslationService } from './translation.service';

/** The browser-storage key that remembers the operator's language choice. */
export const LANGUAGE_STORAGE_KEY = 'codelab-ops.language';

/**
 * The single owner of the console language: keeps the translation catalogue, `<html lang>`, `<html dir>` and the
 * remembered choice in step. The first visit uses config.json's `defaultLanguage`. Browser storage that is missing or
 * refuses (private mode, policy) only means the choice is not remembered.
 */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly document = inject(DOCUMENT);
  private readonly translations = inject(TranslationService);
  private readonly config = inject(OPS_CONFIG);
  private readonly language = signal<AppLanguage>(this.initialLanguage());

  readonly current = this.language.asReadonly();
  readonly dir = computed(() => directionOf(this.language()));
  /** The language the toggle offers. */
  readonly alternate = computed(() => otherLanguage(this.language()));

  /** Applies the initial language. Call once at start-up. */
  init(): Promise<void> {
    return this.apply(this.language());
  }

  /** Switches the language and remembers the choice. */
  set(language: AppLanguage): Promise<void> {
    this.remember(language);
    this.language.set(language);
    return this.apply(language);
  }

  /** Switches to the other shipped language. */
  toggle(): Promise<void> {
    return this.set(this.alternate());
  }

  private async apply(language: AppLanguage): Promise<void> {
    const root = this.document.documentElement;
    root.lang = language;
    root.dir = directionOf(language);
    await this.translations.use(language);
  }

  private initialLanguage(): AppLanguage {
    const stored = this.read();
    return isAppLanguage(stored) ? stored : this.config.defaultLanguage;
  }

  private read(): string | null {
    try {
      return this.document.defaultView?.localStorage.getItem(LANGUAGE_STORAGE_KEY) ?? null;
    } catch {
      return null;
    }
  }

  private remember(language: AppLanguage): void {
    try {
      this.document.defaultView?.localStorage.setItem(LANGUAGE_STORAGE_KEY, language);
    } catch {
      // Storage refused: the choice lasts for this page only, which is the documented behaviour.
    }
  }
}
