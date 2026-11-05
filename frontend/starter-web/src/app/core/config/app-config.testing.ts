import type { Provider } from '@angular/core';
import { DEFAULT_PERMISSION_REFRESH_SECONDS, type AppConfig } from './app-config';
import { APP_CONFIG } from './app-config.service';
import { AppLanguages } from '../i18n/language';

/** A valid runtime configuration for specs. Not used by production code. */
export const TEST_API_BASE_URL = 'http://api.test';

export const TEST_APP_CONFIG: AppConfig = {
  apiBaseUrl: TEST_API_BASE_URL,
  origins: { api: [TEST_API_BASE_URL], attachment: [] },
  security: { protocolPrefix: 'STARTER' },
  defaultLanguage: AppLanguages.Arabic,
  permissionRefreshSeconds: DEFAULT_PERMISSION_REFRESH_SECONDS,
  showcase: false,
  portalContactForm: false,
};

export function provideTestAppConfig(config: AppConfig = TEST_APP_CONFIG): Provider {
  return { provide: APP_CONFIG, useValue: config };
}
