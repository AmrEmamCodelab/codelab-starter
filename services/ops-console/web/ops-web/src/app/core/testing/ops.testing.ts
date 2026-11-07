import { signal, type Provider } from '@angular/core';
import { provideRouter } from '@angular/router';
import { parseApiError, type ApiError } from '@codelab/ng-security';
import { Subject } from 'rxjs';
import type { OpsApiClient } from '../api/ops-api.client';
import { OPS_CONFIG, type OpsConfig } from '../config/ops-config';
import { AppLanguages, type AppLanguage } from '../i18n/language';
import { TranslationService, type TranslationParams } from '../i18n/translation.service';

/**
 * Test doubles for the console specs (spec-only; nothing in the app imports this file). Translations echo their key, and
 * `key|name=value` for parameters, so assertions read the key a template asked for.
 */
export function echoTranslate(key: string, params?: TranslationParams): string {
  if (!params) {
    return key;
  }
  return `${key}|${Object.entries(params).map(([name, value]) => `${name}=${value}`).join(',')}`;
}

/** A TranslationService stand-in: echoes keys, holds a language signal. */
export function translationStub(language: AppLanguage = AppLanguages.English) {
  return { translate: echoTranslate, language: signal(language), use: async () => undefined };
}

export const TEST_CONFIG: OpsConfig = Object.freeze({
  basePath: '/ops/',
  apiBaseUrl: '/ops-api',
  defaultLanguage: AppLanguages.Arabic,
  idpCertificateName: null,
});

/** OPS_CONFIG, the echoing translations and an empty router. */
export function opsTestProviders(config: Partial<OpsConfig> = {}): Provider[] {
  return [
    { provide: OPS_CONFIG, useValue: { ...TEST_CONFIG, ...config } },
    { provide: TranslationService, useValue: translationStub() },
    provideRouter([]),
  ];
}

/** The BFF client calls a spec controls: each method returns a fresh Subject the spec completes or fails. */
export class FakeOpsApiClient implements Pick<OpsApiClient, 'me' | 'health' | 'jobs' | 'certificates' | 'referenceCode'> {
  readonly calls: string[] = [];
  readonly pending = new Map<string, Subject<unknown>>();

  me() {
    return this.next('me');
  }
  health() {
    return this.next('health');
  }
  jobs() {
    return this.next('jobs');
  }
  certificates() {
    return this.next('certificates');
  }
  referenceCode(code: string) {
    return this.next(`reference:${code}`);
  }

  /** Answers the last call to `name` with `value`. */
  answer(name: string, value: unknown): void {
    const subject = this.pending.get(name);
    subject?.next(value);
    subject?.complete();
  }

  /** Fails the last call to `name` with an HTTP-like error. */
  fail(name: string, status: number, body: unknown = null): void {
    this.pending.get(name)?.error({ status, error: body });
  }

  private next(name: string): Subject<never> {
    this.calls.push(name);
    const subject = new Subject<unknown>();
    this.pending.set(name, subject);
    return subject as Subject<never>;
  }
}

/** A problem+json failure with a reference code, as the BFF sends it. */
export function problem(status: number, code: string, correlationId = '7d2c1e9a04b65f38c1a9e2d70b4f6a15'): ApiError {
  return parseApiError({ status, error: { code, correlationId } });
}

/** The trimmed text of the first element matching `selector`, or null. */
export function textOf(root: ParentNode, selector: string): string | null {
  return root.querySelector(selector)?.textContent?.trim() ?? null;
}
