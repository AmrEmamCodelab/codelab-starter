import { provideAppInitializer, provideBrowserGlobalErrorListeners, type ApplicationConfig } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import type { AppConfig } from './core/config/app-config';
import { APP_CONFIG } from './core/config/app-config.service';
import { provideStarterHttp } from './core/http/http.providers';
import { provideStarterSecurity } from './core/security/security.providers';
import { initializeCore } from './core/session/core-initializer';
import { routes } from './app.routes';

/**
 * The application providers, built from the runtime configuration `main.ts` loaded before bootstrap
 * (the signing protocol prefix is needed while the providers are built).
 */
export function createAppConfig(config: AppConfig): ApplicationConfig {
  return {
    providers: [
      provideBrowserGlobalErrorListeners(),
      { provide: APP_CONFIG, useValue: config },
      provideRouter(routes, withComponentInputBinding()),
      provideStarterHttp(),
      ...provideStarterSecurity(config),
      provideAppInitializer(initializeCore),
    ],
  };
}
