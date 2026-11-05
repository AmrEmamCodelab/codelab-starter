import { Injectable, Injector, effect, inject, untracked } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TranslationsClient } from '../api/clients/translations.client';
import { TokenStore } from '../auth/token.store';
import { FeatureFlagNames, FeatureStore } from '../features/feature.store';
import type { AppLanguage } from './language';
import { LanguageService } from './language.service';
import { TranslationService } from './translation.service';

/**
 * Loads the session-gated half of the translations and merges it over the public half.
 *
 * Why two halves: labels name every screen, state, role and action, so a fully public bundle maps the
 * application for anyone. The labels the pre-login pages need stay a static asset; the rest can be
 * served only to a signed-in caller from `GET api/i18n?lang=`. Merged, never replaced, so the public
 * labels stay when a session ends.
 *
 * Off unless the deployment sets the `protectedTranslations` feature flag (the P2 API serves no
 * `api/i18n` yet). Failures are swallowed: a missing label renders as its key, which is ugly but
 * harmless, while an error here would break a screen the user reached successfully.
 */
@Injectable({ providedIn: 'root' })
export class ProtectedTranslationsService {
  private readonly client = inject(TranslationsClient);
  private readonly tokens = inject(TokenStore);
  private readonly features = inject(FeatureStore);
  private readonly language = inject(LanguageService);
  private readonly translations = inject(TranslationService);
  private readonly injector = inject(Injector);

  /** Languages merged for {@link sessionToken}, so toggling back and forth does not refetch. */
  private readonly merged = new Set<AppLanguage>();
  private sessionToken: string | null = null;
  private started = false;

  /** Follows the session, the language and the flag; loads whenever all three allow it. Idempotent. */
  start(): void {
    if (this.started) {
      return;
    }
    this.started = true;
    effect(
      () => {
        const session = this.tokens.session();
        const language = this.language.current();
        const enabled = this.features.isEnabled(FeatureFlagNames.ProtectedTranslations);
        if (session && enabled) {
          untracked(() => void this.load(language, session.accessToken));
        }
      },
      { injector: this.injector },
    );
  }

  /** Fetches and merges `language` for the session holding `accessToken`, once per session. */
  async load(language: AppLanguage, accessToken: string): Promise<void> {
    if (this.sessionToken !== accessToken) {
      this.merged.clear();
      this.sessionToken = accessToken;
    }
    if (this.merged.has(language)) {
      return;
    }
    this.merged.add(language);
    try {
      this.translations.merge(language, await firstValueFrom(this.client.protectedBundle(language)));
    } catch {
      this.merged.delete(language);
    }
  }
}
