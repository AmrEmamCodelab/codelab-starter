import { DOCUMENT, Injectable, computed, inject, signal } from '@angular/core';
import { AppConfigService } from '../config/app-config.service';
import { AppStorage } from '../session/app-storage';
import { StorageKeys } from '../session/storage-keys';
import { directionOf, isAppLanguage, type AppLanguage } from './language';
import { TranslationService } from './translation.service';

/**
 * The single owner of the app language: keeps the translation catalogue, `<html lang>` and
 * `<html dir>` and the remembered choice in step. The first visit uses `config.json`'s
 * `defaultLanguage` (Arabic in the starter); a choice is remembered in browser storage.
 */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly document = inject(DOCUMENT);
  private readonly storage = inject(AppStorage);
  private readonly translations = inject(TranslationService);
  private readonly config = inject(AppConfigService);
  private readonly language = signal<AppLanguage>(this.initialLanguage());

  readonly current = this.language.asReadonly();
  readonly dir = computed(() => directionOf(this.language()));

  /** Applies the initial language. Call once at start-up. */
  init(): Promise<void> {
    return this.apply(this.language());
  }

  /** Switches the language and remembers the choice. */
  set(language: AppLanguage): Promise<void> {
    this.storage.write(StorageKeys.Language, language);
    this.language.set(language);
    return this.apply(language);
  }

  private async apply(language: AppLanguage): Promise<void> {
    const root = this.document.documentElement;
    root.lang = language;
    root.dir = directionOf(language);
    await this.translations.use(language);
  }

  private initialLanguage(): AppLanguage {
    const stored = this.storage.read(StorageKeys.Language);
    return isAppLanguage(stored) ? stored : this.config.defaultLanguage;
  }
}
