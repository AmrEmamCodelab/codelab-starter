import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal, type EnvironmentProviders, type Provider } from '@angular/core';
import { WIRE_ID_PAYLOAD_LENGTH, WIRE_ID_VERSION, asId } from '@codelab/ng-security';
import { provideTestAppConfig } from '../../core/config/app-config.testing';
import { AppLanguages, type AppLanguage } from '../../core/i18n/language';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslationService, type TranslationParams } from '../../core/i18n/translation.service';
import { BROWSER_STORAGE } from '../../core/session/app-storage';
import { MemoryStorage } from '../../core/session/memory-storage';
import {
  ReasonRequirement,
  TransitionSide,
  type RecordAllowedAction,
  type RecordDetail,
  type RecordId,
  type RecordUserId,
  type TimelineEntry,
} from './records.models';

/** Spec fixtures for the generic record UI. Uses the test-only record type `test-record` (prefix TR). Not used by production code. */

export const TEST_RECORD_TYPE = 'test-record';
export const TEST_STATUS_PREFIX = 'testRecords.status';

/** A shape-valid wire id (the browser checks shape only). */
function wireToken(fill: string): string {
  return `${WIRE_ID_VERSION}_k1_${fill.repeat(WIRE_ID_PAYLOAD_LENGTH)}`;
}

export const TEST_RECORD_ID: RecordId = asId('test-record', wireToken('T'));
export const TEST_USER_ID: RecordUserId = asId('public-id-of-users', wireToken('U'));

/** An allowed action, approve-like by default (no reason). */
export function anAction(overrides: Partial<RecordAllowedAction> = {}): RecordAllowedAction {
  return {
    actionKey: 'approve',
    labelKey: 'testRecords.action.approve',
    reason: ReasonRequirement.None,
    reasonFieldKey: null,
    toStatus: 6,
    toTerminal: true,
    ...overrides,
  };
}

/** A reject-like action: Reason Required, field `reason`. */
export function aRequiredReasonAction(overrides: Partial<RecordAllowedAction> = {}): RecordAllowedAction {
  return anAction({ actionKey: 'reject', labelKey: 'testRecords.action.reject', reason: ReasonRequirement.Required, reasonFieldKey: 'reason', toStatus: 7, ...overrides });
}

/** A cancel-like action: Reason Optional. */
export function anOptionalReasonAction(overrides: Partial<RecordAllowedAction> = {}): RecordAllowedAction {
  return anAction({ actionKey: 'cancel', labelKey: 'testRecords.action.cancel', reason: ReasonRequirement.Optional, reasonFieldKey: 'reason', toStatus: 8, ...overrides });
}

/** A record under review with the given allowed actions. */
export function aRecordDetail(overrides: Partial<RecordDetail> = {}): RecordDetail {
  return {
    id: TEST_RECORD_ID,
    recordType: TEST_RECORD_TYPE,
    number: 'TR-2026-001',
    status: 3,
    statusKey: 'under-review',
    wording: {
      requesterKey: `${TEST_STATUS_PREFIX}.under-review.requester`,
      staffKey: `${TEST_STATUS_PREFIX}.under-review.staff`,
      adminKey: `${TEST_STATUS_PREFIX}.under-review.admin`,
    },
    isTerminal: false,
    ownerDisplayName: 'Requester One',
    createdAt: '2026-11-01T08:00:00Z',
    submittedAt: '2026-11-01T09:00:00Z',
    updatedAt: '2026-11-02T10:00:00Z',
    lastReason: null,
    allowedActions: [anAction(), aRequiredReasonAction()],
    assignee: null,
    sla: null,
    ...overrides,
  };
}

/** A timeline entry. */
export function anEntry(overrides: Partial<TimelineEntry> = {}): TimelineEntry {
  return {
    at: '2026-11-01T09:00:00Z',
    actorDisplayName: 'Requester One',
    side: TransitionSide.Requester,
    actionKey: 'submit',
    fromStatus: 1,
    toStatus: 2,
    toStatusKey: 'submitted',
    reason: null,
    ...overrides,
  };
}

/** The text the fake translation service renders: the key, then the parameters as JSON when there are any. */
export function translated(key: string, params?: TranslationParams): string {
  return params ? `${key}${JSON.stringify(params)}` : key;
}

/** A translation service that renders {@link translated}, and a fixed language. */
export function recordsUiTestProviders(language: AppLanguage = AppLanguages.English): Provider[] {
  return [
    { provide: TranslationService, useValue: { translate: translated } },
    { provide: LanguageService, useValue: { current: signal(language) } },
  ];
}

/** The test app config, the HTTP testing backend and memory storage, for the client specs. */
export function recordsHttpTestProviders(): (Provider | EnvironmentProviders)[] {
  return [
    provideTestAppConfig(),
    provideHttpClient(),
    provideHttpClientTesting(),
    { provide: BROWSER_STORAGE, useValue: new MemoryStorage() },
  ];
}
