import { signal, type Provider } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject, of, type Observable } from 'rxjs';
import { vi } from 'vitest';
import ar from '../../../assets/i18n/ar.json';
import en from '../../../assets/i18n/en.json';
import { STATIC_JSON_FETCH } from '../../core/http/static-json.loader';
import { AppLanguages, type AppLanguage } from '../../core/i18n/language';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslationService } from '../../core/i18n/translation.service';
import { PermissionStore } from '../../core/permissions/permission.store';
import { AttachmentError } from '../attachments/attachment-error';
import { TEST_ATTACHMENT, TEST_TYPE_KEY } from '../attachments/attachments.testing';
import type {
  AttachmentType,
  AttachmentUploadEvent,
  RecordAttachment,
  RecordAttachmentList,
} from '../attachments/attachments.models';
import { RecordAttachmentsAdapter } from '../attachments/record-attachments.adapter';
import { AttachmentsPermissionKeys } from './attachments-permission-keys';

/** Spec fixtures for `app-record-attachments`. Not used by production code. */

export const TEST_TYPE: AttachmentType = {
  key: TEST_TYPE_KEY,
  nameAr: 'مستند تجريبي',
  nameEn: 'Demo document',
  isRequired: true,
  maxFiles: 5,
  allowedExtensions: ['.pdf', '.png', '.jpg', '.jpeg'],
};

export const OPTIONAL_TYPE: AttachmentType = {
  key: 'other-document',
  nameAr: 'مستند آخر',
  nameEn: 'Other document',
  isRequired: false,
  maxFiles: 1,
  allowedExtensions: ['.pdf'],
};

export const ALL_ATTACHMENT_PERMISSIONS: readonly string[] = Object.values(AttachmentsPermissionKeys);

export const TEST_REFERENCE_CODE = 'R7K2-9QX4';

export function anAttachment(overrides: Partial<RecordAttachment> = {}): RecordAttachment {
  return { ...TEST_ATTACHMENT, ...overrides };
}

export function aList(items: readonly RecordAttachment[] = [], missing: readonly string[] = []): RecordAttachmentList {
  return { items, missingRequiredTypeKeys: missing };
}

/** The service's refusal of an oversized file. */
export function tooLarge(): AttachmentError {
  return new AttachmentError('ATTACHMENTS_FILE_TOO_LARGE', TEST_REFERENCE_CODE, 413);
}

/**
 * A fake {@link RecordAttachmentsAdapter}: `types` and `list` answer the given values; every `upload` call returns a
 * fresh Subject the spec drives (`uploads[n]`); `download` and `remove` answer at once unless replaced.
 */
export function fakeAdapter(types: readonly AttachmentType[] = [TEST_TYPE], list: RecordAttachmentList = aList([], [TEST_TYPE_KEY])) {
  const uploads: Subject<AttachmentUploadEvent>[] = [];
  return {
    uploads,
    types: vi.fn((): Observable<readonly AttachmentType[]> => of(types)),
    list: vi.fn((): Observable<RecordAttachmentList> => of(list)),
    upload: vi.fn((): Observable<AttachmentUploadEvent> => {
      const subject = new Subject<AttachmentUploadEvent>();
      uploads.push(subject);
      return subject;
    }),
    download: vi.fn((): Observable<void> => of(undefined)),
    remove: vi.fn((): Observable<void> => of(undefined)),
  };
}

export type FakeAdapter = ReturnType<typeof fakeAdapter>;

/**
 * Providers for the widget: the fake adapter, the real translation bundles (`ar`/`en`), a fixed language and the held
 * permission keys.
 */
export function recordAttachmentsTestProviders(
  adapter: FakeAdapter,
  language: AppLanguage = AppLanguages.Arabic,
  heldKeys: readonly string[] = ALL_ATTACHMENT_PERMISSIONS,
): Provider[] {
  const bundles: Readonly<Record<string, unknown>> = { ar, en };
  return [
    { provide: RecordAttachmentsAdapter, useValue: adapter },
    {
      provide: STATIC_JSON_FETCH,
      useValue: (url: string) => Promise.resolve(bundles[url.endsWith(`${AppLanguages.English}.json`) ? AppLanguages.English : AppLanguages.Arabic]),
    },
    { provide: LanguageService, useValue: { current: signal(language) } },
    { provide: PermissionStore, useValue: { has: (key: string) => heldKeys.includes(key) } },
  ];
}

/** Loads the real bundle of `language` into the translation service (call after `configureTestingModule`). */
export function useLanguage(language: AppLanguage): Promise<void> {
  return TestBed.inject(TranslationService).use(language);
}
