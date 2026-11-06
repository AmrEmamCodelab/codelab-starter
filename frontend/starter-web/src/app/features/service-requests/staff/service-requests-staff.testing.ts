import { HttpErrorResponse } from '@angular/common/http';
import { Component, booleanAttribute, input, signal, type EnvironmentProviders, type Provider } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { WIRE_ID_PAYLOAD_LENGTH, WIRE_ID_VERSION, asId } from '@codelab/ng-security';
import { AuthService } from '../../../core/auth/auth.service';
import { AppLanguages } from '../../../core/i18n/language';
import { LanguageService } from '../../../core/i18n/language.service';
import { TranslationService } from '../../../core/i18n/translation.service';
import type { PermissionKey } from '../../../core/permissions/permission-keys.generated';
import { PermissionStore } from '../../../core/permissions/permission.store';
import {
  ReasonRequirement,
  SlaState,
  TransitionSide,
  type OversightListItem,
  type RecordAllowedAction,
  type RecordAssignee,
  type RecordDetail,
  type RecordUserId,
  type TimelineEntry,
  type WorkQueueItem,
} from '../../../shared/records';
import { translated } from '../../../shared/records/records.testing';
import { ServiceRequestActions, ServiceRequestStatus, SERVICE_REQUEST_RECORD_TYPE } from '../data';
import { TEST_SERVICE_REQUEST_ID } from '../data/service-requests.testing';

/** Spec fixtures and helpers of the staff and admin screens. Spec-only: nothing in the app imports this file. */

function wireToken(fill: string): string {
  return `${WIRE_ID_VERSION}_k1_${fill.repeat(WIRE_ID_PAYLOAD_LENGTH)}`;
}

export const CURRENT_USER_ID: RecordUserId = asId('public-id-of-users', wireToken('M'));
export const OTHER_USER_ID: RecordUserId = asId('public-id-of-users', wireToken('O'));
export const SECOND_REQUEST_ID = asId('service-request', wireToken('Z'));

/** The signed-in staff member. */
export const CURRENT_USER = { id: CURRENT_USER_ID, displayName: 'Staff One' };

export function assignee(overrides: Partial<RecordAssignee> = {}): RecordAssignee {
  return { userId: CURRENT_USER_ID, displayName: 'Staff One', assignedAt: '2026-11-03T08:00:00Z', ...overrides };
}

export function queueItem(overrides: Partial<WorkQueueItem> = {}): WorkQueueItem {
  return {
    id: TEST_SERVICE_REQUEST_ID,
    number: 'SR-2026-001',
    status: ServiceRequestStatus.Submitted,
    statusKey: 'submitted',
    submittedAt: '2026-11-02T09:00:00Z',
    ownerDisplayName: 'Requester One',
    assignee: null,
    sla: null,
    ...overrides,
  };
}

export function oversightItem(overrides: Partial<OversightListItem> = {}): OversightListItem {
  return {
    id: TEST_SERVICE_REQUEST_ID,
    recordType: SERVICE_REQUEST_RECORD_TYPE,
    number: 'SR-2026-001',
    status: ServiceRequestStatus.UnderReview,
    statusKey: 'under-review',
    createdAt: '2026-11-01T08:00:00Z',
    submittedAt: '2026-11-02T09:00:00Z',
    updatedAt: '2026-11-03T10:00:00Z',
    isTerminal: false,
    ownerDisplayName: 'Requester One',
    ...overrides,
  };
}

export function allowedAction(actionKey: string, reason: ReasonRequirement = ReasonRequirement.None): RecordAllowedAction {
  return {
    actionKey,
    labelKey: `serviceRequests.action.${actionKey}`,
    reason,
    reasonFieldKey: reason === ReasonRequirement.None ? null : 'reason',
    toStatus: ServiceRequestStatus.Approved,
    toTerminal: false,
  };
}

/** The decisions the server allows a decider on a request under review: return and reject need a reason. */
export function reviewDecisions(): readonly RecordAllowedAction[] {
  return [
    allowedAction(ServiceRequestActions.Approve),
    allowedAction(ServiceRequestActions.Return, ReasonRequirement.Required),
    allowedAction(ServiceRequestActions.Reject, ReasonRequirement.Required),
  ];
}

function wording(statusKey: string) {
  return {
    requesterKey: `serviceRequests.status.${statusKey}.requester`,
    staffKey: `serviceRequests.status.${statusKey}.staff`,
    adminKey: `serviceRequests.status.${statusKey}.admin`,
  };
}

/** A request under review, held by the signed-in user, with the review decisions allowed. */
export function staffRecord(overrides: Partial<RecordDetail> = {}): RecordDetail {
  return {
    id: TEST_SERVICE_REQUEST_ID,
    recordType: SERVICE_REQUEST_RECORD_TYPE,
    number: 'SR-2026-001',
    status: ServiceRequestStatus.UnderReview,
    statusKey: 'under-review',
    wording: wording('under-review'),
    isTerminal: false,
    ownerDisplayName: 'Requester One',
    createdAt: '2026-11-01T08:00:00Z',
    submittedAt: '2026-11-02T09:00:00Z',
    updatedAt: '2026-11-03T10:00:00Z',
    lastReason: null,
    allowedActions: reviewDecisions(),
    assignee: assignee(),
    sla: { state: SlaState.OnTrack, dueAt: '2026-11-06T09:00:00Z' },
    ...overrides,
  };
}

/** The same request in another status (wording follows), terminal for 6, 7 and 8 with no allowed action. */
export function recordIn(status: ServiceRequestStatus, statusKey: string, overrides: Partial<RecordDetail> = {}): RecordDetail {
  const terminal = status === ServiceRequestStatus.Approved || status === ServiceRequestStatus.Rejected || status === ServiceRequestStatus.Cancelled;
  return staffRecord({ status, statusKey, wording: wording(statusKey), isTerminal: terminal, allowedActions: terminal ? [] : reviewDecisions(), ...overrides });
}

export function timelineEntry(overrides: Partial<TimelineEntry> = {}): TimelineEntry {
  return {
    at: '2026-11-02T09:00:00Z',
    actorDisplayName: 'Requester One',
    side: TransitionSide.Requester,
    actionKey: ServiceRequestActions.Submit,
    fromStatus: ServiceRequestStatus.Draft,
    toStatus: ServiceRequestStatus.Submitted,
    toStatusKey: 'submitted',
    reason: null,
    ...overrides,
  };
}

/** A problem+json failure as the Angular HTTP client raises it. */
export function problem(status: number, code: string): HttpErrorResponse {
  return new HttpErrorResponse({ status, error: { code, correlationId: 'trace-1', referenceCode: 'REF-7' } });
}

/** Stands in for `app-record-attachments` (it loads over HTTP); it records the inputs it was given. */
@Component({ selector: 'app-record-attachments', template: '<p class="attachments-stub">{{ recordType() }}|{{ readonly() }}</p>' })
export class RecordAttachmentsStubComponent {
  readonly recordType = input('');
  readonly recordId = input<string | null>(null);
  readonly readonly = input(false, { transform: booleanAttribute });
}

/** Echo translations (`key{json params}`), an English fake language, an empty router and the signed-in staff member. */
export function staffTestProviders(): (Provider | EnvironmentProviders)[] {
  return [
    provideRouter([]),
    { provide: TranslationService, useValue: { translate: translated, language: signal(AppLanguages.English), has: () => true } },
    { provide: LanguageService, useValue: { current: signal(AppLanguages.English) } },
    { provide: AuthService, useValue: { user: signal(CURRENT_USER), isAuthenticated: () => true } },
  ];
}

/** Grants exactly these keys to the signed-in user. */
export function grant(...keys: PermissionKey[]): void {
  TestBed.inject(PermissionStore).set(keys);
}

/** Lets pending promises and effects settle, then re-renders. */
export async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  for (let round = 0; round < SETTLE_ROUNDS; round += 1) {
    TestBed.tick();
    await fixture.whenStable();
    fixture.detectChanges();
  }
}

/** Lets a store's effects and promises settle (no component). */
export async function flush(): Promise<void> {
  for (let round = 0; round < SETTLE_ROUNDS; round += 1) {
    TestBed.tick();
    await Promise.resolve();
    await new Promise<void>((resolve) => setTimeout(resolve, 0));
  }
}

/** Enough rounds for a click -> async store call -> signal write -> effect -> render chain. */
const SETTLE_ROUNDS = 4;

/** Every element with `data-testid`. */
export function allByTestId(root: HTMLElement, id: string): HTMLElement[] {
  return [...root.querySelectorAll<HTMLElement>(`[data-testid="${id}"]`)];
}

/** Clicks the native button inside a `dga-button` (or the element itself). */
export function press(element: Element | null | undefined): void {
  const target = element?.querySelector('button') ?? element;
  if (!(target instanceof HTMLElement)) {
    throw new Error('Nothing to press.');
  }
  target.click();
}
