import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
  type ApplicationConfig,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { OPS_CONFIG, type OpsConfig } from './core/config/ops-config';
import { accessInterceptor } from './core/http/access.interceptor';
import { LanguageService } from './core/i18n/language.service';

/** The providers, with the validated runtime config. The initial language is applied before the first render. */
export function createAppConfig(config: OpsConfig): ApplicationConfig {
  return {
    providers: [
      provideBrowserGlobalErrorListeners(),
      provideZonelessChangeDetection(),
      { provide: OPS_CONFIG, useValue: config },
      provideHttpClient(withInterceptors([accessInterceptor])),
      provideRouter(routes, withComponentInputBinding()),
      provideAppInitializer(() => inject(LanguageService).init()),
    ],
  };
}
