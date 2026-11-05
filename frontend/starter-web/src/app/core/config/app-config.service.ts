import { Injectable, InjectionToken, inject } from '@angular/core';
import type { DgaBranding } from '@codelab/ng-dga';
import { joinUrl, type AppConfig, type AppConfigOrigins } from './app-config';
import type { AppLanguage } from '../i18n/language';

/** The configuration `main.ts` loaded before bootstrap; provided by `app.config.ts`. */
export const APP_CONFIG = new InjectionToken<AppConfig>('APP_CONFIG');

/**
 * Read access to the runtime configuration. It is loaded before Angular bootstraps (the signing
 * protocol prefix is needed while the providers are built), so every getter is safe from the first
 * injection on.
 */
@Injectable({ providedIn: 'root' })
export class AppConfigService {
  private readonly config = inject(APP_CONFIG);

  get apiBaseUrl(): string {
    return this.config.apiBaseUrl;
  }

  get origins(): AppConfigOrigins {
    return this.config.origins;
  }

  get protocolPrefix(): string {
    return this.config.security.protocolPrefix;
  }

  get defaultLanguage(): AppLanguage {
    return this.config.defaultLanguage;
  }

  /** Seconds between background permission syncs; 0 means no interval. */
  get permissionRefreshSeconds(): number {
    return this.config.permissionRefreshSeconds;
  }

  /** The project branding from `config.json`, or undefined for the DGA default look. */
  get branding(): DgaBranding | undefined {
    return this.config.branding;
  }

  /** True only when `config.json` turns the `/showcase` pages on (staging) or the build is a development build. */
  get showcase(): boolean {
    return this.config.showcase === true;
  }

  /** True only when `config.json` turns the public contact form on (its API endpoint is deployed). */
  get portalContactForm(): boolean {
    return this.config.portalContactForm === true;
  }

  /** Resolves a path against the API root. */
  apiUrl(path: string): string {
    return joinUrl(this.apiBaseUrl, path);
  }
}
