import { HttpErrorResponse } from '@angular/common/http';
import { Component, input, signal, type EnvironmentProviders, type Provider } from '@angular/core';
import { provideRouter } from '@angular/router';
import { WIRE_ID_PAYLOAD_LENGTH, WIRE_ID_VERSION, asId } from '@codelab/ng-security';
import { NEVER, of, throwError, type Observable } from 'rxjs';
import { vi } from 'vitest';
import { AppLanguages, type AppLanguage } from '../../../core/i18n/language';
import { LanguageService } from '../../../core/i18n/language.service';
import { TranslationService, type TranslationParams } from '../../../core/i18n/translation.service';
import { PermissionStore } from '../../../core/permissions/permission.store';
import { APP_CLOCK } from '../../../core/session/clock';
import {
  ReasonRequirement,
  RecordsClient,
  TransitionSide,
  type RecordAllowedAction,
  type RecordDetail,
  type RecordListItem,
  type RecordPage,
  type TimelineEntry,
} from '../../../shared/records';
import {
  SERVICE_REQUEST_RECORD_TYPE,
  SERVICE_REQUESTS_PERMISSION_KEYS,
  ServiceRequestActions,
  ServiceRequestStatus,
  ServiceRequestsClient,
  type ServiceRequestDetails,
  type ServiceRequestDraft,
  type ServiceRequestId,
} from '../data';
import {
  TEST_ATTACHMENTS_RECORD_ID,
  TEST_CATEGORY_ID,
  TEST_SERVICE_REQUEST_ID,
  recordAttachmentsWire,
  serviceRequestDetails,
  type RecordAttachmentsWireFields,
} from '../data/service-requests.testing';
import { SERVICE_REQUEST_STATUS_KEYS } from './requester-status';
import { ServiceRequestCategoriesClient, type ServiceRequestCategory } from './service-request-categories.client';

/** Spec fixtures of the requester screens. Not used by production code. */

export { TEST_ATTACHMENTS_RECORD_ID, TEST_CATEGORY_ID, TEST_SERVICE_REQUEST_ID, serviceRequestDetails };

/** A record as the generic records endpoint sends it: the shared shape plus the attachment fields. */
export type ServiceRequestWireRecord = RecordDetail & RecordAttachmentsWireFields;

/** 2026-11-06 10:00 in Riyadh (07:00 UTC): the parity fixture's "today". */
export const TEST_NOW_MS = Date.UTC(2026, 10, 6, 7, 0, 0);
export const TEST_TODAY = '2026-11-06';

function wireToken(fill: string): string {
  return `${WIRE_ID_VERSION}_k1_${fill.repeat(WIRE_ID_PAYLOAD_LENGTH)}`;
}

export const OTHER_SERVICE_REQUEST_ID: ServiceRequestId = asId('service-request', wireToken('O'));

/** A placeholder mobile number of the accepted shape (built, so no phone-like literal sits in the repository). */
export const TEST_PHONE = `05${'0'.repeat(8)}`;

/** The text the fake translation service renders: the key, then the parameters as JSON when there are any. */
export function translated(key: string, params?: TranslationParams): string {
  return params ? `${key}${JSON.stringify(params)}` : key;
}

/** One list row of the sample record type. */
export function aListItem(overrides: Partial<RecordListItem> = {}): RecordListItem {
  return {
    id: TEST_SERVICE_REQUEST_ID,
    recordType: SERVICE_REQUEST_RECORD_TYPE,
    number: 'SR-2026-001',
    status: ServiceRequestStatus.Submitted,
    statusKey: SERVICE_REQUEST_STATUS_KEYS[ServiceRequestStatus.Submitted],
    createdAt: '2026-11-01T08:00:00Z',
    submittedAt: '2026-11-01T09:00:00Z',
    updatedAt: '2026-11-01T09:00:00Z',
    isTerminal: false,
    ...overrides,
  };
}

/** A page of rows. */
export function aPage(items: readonly RecordListItem[], total: number = items.length): RecordPage<RecordListItem> {
  return { items, page: 1, pageSize: 20, total };
}

/** The wording keys of a status, as the catalog writes them. */
function wordingOf(status: ServiceRequestStatus) {
  const key = SERVICE_REQUEST_STATUS_KEYS[status];
  return {
    requesterKey: `serviceRequests.status.${key}.requester`,
    staffKey: `serviceRequests.status.${key}.staff`,
    adminKey: `serviceRequests.status.${key}.admin`,
  };
}

/** The requester's cancel action (Reason Optional). */
export function cancelAction(): RecordAllowedAction {
  return {
    actionKey: ServiceRequestActions.Cancel,
    labelKey: 'serviceRequests.action.cancel',
    reason: ReasonRequirement.Optional,
    reasonFieldKey: 'reason',
    toStatus: ServiceRequestStatus.Cancelled,
    toTerminal: true,
  };
}

/** The requester's resubmit action (no reason). */
export function resubmitAction(): RecordAllowedAction {
  return {
    actionKey: ServiceRequestActions.Resubmit,
    labelKey: 'serviceRequests.action.resubmit',
    reason: ReasonRequirement.None,
    reasonFieldKey: null,
    toStatus: ServiceRequestStatus.Resubmitted,
    toTerminal: false,
  };
}

/** The generic record of a request in `status` (submitted, cancellable, by default), with its attachments id. */
export function aRecord(
  status: ServiceRequestStatus = ServiceRequestStatus.Submitted,
  overrides: Partial<ServiceRequestWireRecord> = {},
): ServiceRequestWireRecord {
  const terminal = status === ServiceRequestStatus.Approved || status === ServiceRequestStatus.Rejected || status === ServiceRequestStatus.Cancelled;
  return {
    id: TEST_SERVICE_REQUEST_ID,
    recordType: SERVICE_REQUEST_RECORD_TYPE,
    number: status === ServiceRequestStatus.Draft ? null : 'SR-2026-001',
    status,
    statusKey: SERVICE_REQUEST_STATUS_KEYS[status],
    wording: wordingOf(status),
    isTerminal: terminal,
    ownerDisplayName: 'Requester One',
    createdAt: '2026-11-01T08:00:00Z',
    submittedAt: status === ServiceRequestStatus.Draft ? null : '2026-11-01T09:00:00Z',
    updatedAt: '2026-11-02T10:00:00Z',
    lastReason: null,
    allowedActions: terminal ? [] : [cancelAction()],
    assignee: null,
    sla: null,
    ...recordAttachmentsWire(),
    ...overrides,
  };
}

/** A timeline step. */
export function aStep(overrides: Partial<TimelineEntry> = {}): TimelineEntry {
  return {
    at: '2026-11-01T09:00:00Z',
    actorDisplayName: 'Requester One',
    side: TransitionSide.Requester,
    actionKey: ServiceRequestActions.Submit,
    fromStatus: ServiceRequestStatus.Draft,
    toStatus: ServiceRequestStatus.Submitted,
    toStatusKey: SERVICE_REQUEST_STATUS_KEYS[ServiceRequestStatus.Submitted],
    reason: null,
    ...overrides,
  };
}

/** A saved draft (`GET .../draft`). */
export function aDraft(overrides: Partial<ServiceRequestDraft> = {}): ServiceRequestDraft {
  return {
    id: TEST_SERVICE_REQUEST_ID,
    categoryId: null,
    title: null,
    description: null,
    contactPhone: null,
    preferredDate: null,
    ...overrides,
  };
}

export const TEST_CATEGORY: ServiceRequestCategory = {
  id: TEST_CATEGORY_ID,
  code: 'certificates',
  nameAr: 'شهادات',
  nameEn: 'Certificates',
  sortOrder: 1,
};

/** A problem+json refusal as the HTTP layer delivers it. */
export function httpRefusal(status: number, code: string, errors?: Readonly<Record<string, readonly string[]>>): HttpErrorResponse {
  return new HttpErrorResponse({
    status,
    error: { type: 'about:blank', title: code, status, code, referenceCode: 'R7K2-9QX4', ...(errors ? { errors } : {}) },
  });
}

/** An observable that fails with `error`. */
export function failing<T>(error: unknown): Observable<T> {
  return throwError(() => error);
}

/** A fake RecordsClient: every call answers the given values (spies). */
export function fakeRecordsClient(overrides: Partial<Record<keyof RecordsClient, ReturnType<typeof vi.fn>>> = {}) {
  return {
    mine: vi.fn(() => of(aPage([aListItem()]))),
    detail: vi.fn(() => of(aRecord())),
    timeline: vi.fn(() => of([aStep()])),
    transition: vi.fn(() => of(aRecord())),
    oversight: vi.fn(() => NEVER),
    ...overrides,
  };
}

/** A fake ServiceRequestsClient. */
export function fakeServiceRequestsClient(overrides: Partial<Record<keyof ServiceRequestsClient, ReturnType<typeof vi.fn>>> = {}) {
  return {
    create: vi.fn(() => of({ id: TEST_SERVICE_REQUEST_ID })),
    getDraft: vi.fn(() => of(aDraft())),
    saveDraft: vi.fn(() => of(undefined)),
    submit: vi.fn(() => of({ number: 'SR-2026-001' })),
    get: vi.fn((): Observable<ServiceRequestDetails> => of(serviceRequestDetails())),
    ...overrides,
  };
}

/** A fake categories client answering one category. */
export function fakeCategoriesClient(categories: readonly ServiceRequestCategory[] = [TEST_CATEGORY]) {
  return { list: vi.fn(() => of(categories)) };
}

export const ALL_SERVICE_REQUEST_PERMISSIONS: readonly string[] = Object.values(SERVICE_REQUESTS_PERMISSION_KEYS);

/** What the screens need around them: echo translations, a fixed language and clock, held keys and a router. */
export function requesterTestProviders(
  options: { readonly language?: AppLanguage; readonly held?: readonly string[] } = {},
): (Provider | EnvironmentProviders)[] {
  const { language = AppLanguages.English, held = ALL_SERVICE_REQUEST_PERMISSIONS } = options;
  const languageSignal = signal(language);
  return [
    provideRouter([]),
    { provide: TranslationService, useValue: { translate: translated, has: () => true, language: languageSignal } },
    { provide: LanguageService, useValue: { current: languageSignal } },
    { provide: PermissionStore, useValue: { has: (key: string) => held.includes(key) } },
    { provide: APP_CLOCK, useValue: () => TEST_NOW_MS },
  ];
}

/** Client providers over the given fakes. */
export function clientProviders(fakes: {
  readonly records?: ReturnType<typeof fakeRecordsClient>;
  readonly requests?: ReturnType<typeof fakeServiceRequestsClient>;
  readonly categories?: ReturnType<typeof fakeCategoriesClient>;
}): Provider[] {
  return [
    { provide: RecordsClient, useValue: fakes.records ?? fakeRecordsClient() },
    { provide: ServiceRequestsClient, useValue: fakes.requests ?? fakeServiceRequestsClient() },
    { provide: ServiceRequestCategoriesClient, useValue: fakes.categories ?? fakeCategoriesClient() },
  ];
}

/** Stands in for `app-record-attachments` (its own spec covers it): records what it was given. */
@Component({ selector: 'app-record-attachments', template: '<span class="stub-attachments"></span>' })
export class StubRecordAttachmentsComponent {
  readonly recordType = input.required<string>();
  readonly recordId = input.required<string>();
  readonly readonly = input(false);
}
