import { inject } from '@angular/core';
import { AuthService } from '../auth/auth.service';
import { FeatureStore } from '../features/feature.store';
import { LanguageService } from '../i18n/language.service';
import { ProtectedTranslationsService } from '../i18n/protected-translations.service';
import { PermissionSyncService } from '../permissions/permission-sync.service';
import { SessionTimeoutService } from './session-timeout.service';

/**
 * The start-up sequence, run by `provideAppInitializer` in `app.config.ts`: apply the language (so the
 * first paint has its labels and direction), load the feature flags, start the session watchers and,
 * for a restored session, sync the permissions. Nothing here fails start-up: each step degrades on its
 * own (keys show, flags off, guards re-sync).
 */
export async function initializeCore(): Promise<void> {
  const language = inject(LanguageService);
  const features = inject(FeatureStore);
  const auth = inject(AuthService);
  const permissions = inject(PermissionSyncService);
  inject(SessionTimeoutService).start();
  inject(ProtectedTranslationsService).start();
  await Promise.all([language.init(), features.load()]);
  if (auth.isAuthenticated()) {
    await permissions.ensureLoaded();
  }
}
