import { HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import { signal, type EnvironmentProviders, type Provider } from '@angular/core';
import { provideRouter } from '@angular/router';
import { NEVER, of, throwError, type Observable } from 'rxjs';
import { AppLanguages, type AppLanguage } from '../../core/i18n/language';
import { TranslationService, type TranslationParams } from '../../core/i18n/translation.service';
import { echoTranslate } from '../../patterns/patterns.testing';
import {
  PortalContentClient,
  type PortalContentDto,
  type PortalContentPage,
  type PortalContentSectionDto,
} from './content/portal-content.client';

/**
 * Test doubles for the portal specs (spec-only; nothing in the app imports this file). Translations echo their key
 * (and `key|name=value` for parameters), unless the spec gives texts for some keys.
 */
export class FakePortalTranslations {
  readonly language = signal<AppLanguage>(AppLanguages.Arabic);
  private readonly texts = new Map<string, string>();

  /** Gives `key` a real text (search specs match on it); later calls win. */
  define(entries: Readonly<Record<string, string>>): void {
    for (const [key, text] of Object.entries(entries)) {
      this.texts.set(key, text);
    }
  }

  translate(key: string, params?: TranslationParams): string {
    const text = this.texts.get(key);
    return text === undefined ? echoTranslate(key, params) : text;
  }

  /** Only `portal.*` keys and keys given through {@link define} count as translated. */
  has(key: string): boolean {
    return this.texts.has(key) || key.startsWith('portal.');
  }
}

/** A fresh fake, its provider and the router every portal page needs. */
export function portalTestProviders(translations = new FakePortalTranslations()): (Provider | EnvironmentProviders)[] {
  return [{ provide: TranslationService, useValue: translations }, provideRouter([])];
}

/** The text directions a page spec renders in (Arabic first). */
export const TEST_DIRECTIONS: readonly ['rtl' | 'ltr', AppLanguage][] = [
  ['rtl', AppLanguages.Arabic],
  ['ltr', AppLanguages.English],
];

/** A content section with sensible defaults; override any field. */
export function aContentSection(overrides: Partial<PortalContentSectionDto> = {}): PortalContentSectionDto {
  return {
    id: 'section-public-id',
    titleAr: 'عنوان',
    titleEn: 'Title',
    bodyAr: 'نص',
    bodyEn: 'Body',
    sortOrder: 1,
    ...overrides,
  };
}

/**
 * A stand-in for `PortalContentClient`: answers each page with the sections given through {@link answer}, or fails it
 * (status 503, or `status` given) after {@link fail}. A page with neither stays pending (the loading state).
 */
export class FakePortalContentClient {
  readonly requests: PortalContentPage[] = [];
  private readonly settled = new Map<PortalContentPage, () => Observable<PortalContentDto>>();

  answer(page: PortalContentPage, sections: readonly PortalContentSectionDto[]): void {
    this.settled.set(page, () => of({ page, updatedAt: null, sections }));
  }

  fail(page: PortalContentPage, status = HttpStatusCode.ServiceUnavailable): void {
    this.settled.set(page, () => throwError(() => new HttpErrorResponse({ status })));
  }

  page(page: PortalContentPage): Observable<PortalContentDto> {
    this.requests.push(page);
    const settled = this.settled.get(page);
    if (settled) {
      return settled();
    }
    return NEVER;
  }
}

/** {@link portalTestProviders} plus a {@link FakePortalContentClient} as the content client. */
export function portalContentTestProviders(
  content: FakePortalContentClient,
  translations = new FakePortalTranslations(),
): (Provider | EnvironmentProviders)[] {
  return [...portalTestProviders(translations), { provide: PortalContentClient, useValue: content }];
}
