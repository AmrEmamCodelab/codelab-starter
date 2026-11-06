import { describe, expect, it } from 'vitest';
import { ActionTone } from '../../../patterns';
import { QueueView, ReasonRequirement, WORKFLOW_MAX_REASON_LENGTH, WorkflowAudience } from '../../../shared/records';
import { ServiceRequestActions, ServiceRequestStatus } from '../data';
import { TEST_SERVICE_REQUEST_ID } from '../data/service-requests.testing';
import {
  ALL_SERVICE_REQUEST_STATUSES,
  SERVICE_REQUEST_STATUS_KEYS,
  STAFF_QUEUE_STATUSES,
  serviceRequestActionLabelKey,
  serviceRequestActionTone,
  serviceRequestStatusTone,
  serviceRequestStatusWording,
} from './service-request-status-display';
import { serviceRequestDetailCards, serviceRequestRecordCard } from './service-request-record-card';
import { SERVICE_REQUEST_NOT_FOUND, serviceRequestIdFromRoute } from './service-request-route-id';
import { ServiceRequestsStaffTranslationKeys } from './service-requests-staff-translation-keys';
import { AssignmentHolder, STAFF_REASON_LIMITS, assignmentHolder, findRecordAction, staffDecisionButtons } from './staff-decisions';
import { asQueueView, initialQueueView, staffQueueTabs } from './staff-queue-views';
import { CURRENT_USER_ID, OTHER_USER_ID, allowedAction, assignee, staffRecord } from './service-requests-staff.testing';
import { serviceRequestDetails } from '../data/service-requests.testing';

const RecordKeys = ServiceRequestsStaffTranslationKeys.Record;

describe('staff decision buttons', () => {
  it('maps every allowed action, in the server order, and asks for a reason only when it is Required', () => {
    const buttons = staffDecisionButtons([
      allowedAction(ServiceRequestActions.Approve),
      allowedAction(ServiceRequestActions.Return, ReasonRequirement.Required),
      allowedAction(ServiceRequestActions.Cancel, ReasonRequirement.Optional),
    ]);
    expect(buttons.map((button) => [button.code, button.requiresReason, button.tone])).toEqual([
      [ServiceRequestActions.Approve, false, ActionTone.Primary],
      [ServiceRequestActions.Return, true, ActionTone.Neutral],
      [ServiceRequestActions.Cancel, false, ActionTone.Destructive],
    ]);
    expect(buttons[0].labelKey).toBe('serviceRequests.action.approve');
  });

  it('adds nothing for an empty list', () => {
    expect(staffDecisionButtons([])).toEqual([]);
  });

  it('finds an allowed action by key, or null once it is gone', () => {
    const actions = [allowedAction(ServiceRequestActions.Approve)];
    expect(findRecordAction(actions, ServiceRequestActions.Approve)?.actionKey).toBe(ServiceRequestActions.Approve);
    expect(findRecordAction(actions, ServiceRequestActions.Reject)).toBeNull();
  });

  it('uses the workflow limits for the reason: any non-blank text up to the maximum', () => {
    expect(STAFF_REASON_LIMITS).toEqual({ minLength: 1, maxLength: WORKFLOW_MAX_REASON_LENGTH });
  });
});

describe('assignment holder', () => {
  it('tells nobody, the signed-in user and someone else apart', () => {
    expect(assignmentHolder(null, CURRENT_USER_ID)).toBe(AssignmentHolder.Nobody);
    expect(assignmentHolder(assignee(), CURRENT_USER_ID)).toBe(AssignmentHolder.CurrentUser);
    expect(assignmentHolder(assignee({ userId: OTHER_USER_ID }), CURRENT_USER_ID)).toBe(AssignmentHolder.SomeoneElse);
  });

  it('never treats a record as the user\'s own without a signed-in user', () => {
    expect(assignmentHolder(assignee(), null)).toBe(AssignmentHolder.SomeoneElse);
  });
});

describe('queue views', () => {
  it('offers unassigned, mine and all to a worker, and opens on unassigned', () => {
    const tabs = staffQueueTabs(true);
    expect(tabs.map((tab) => tab.value)).toEqual([QueueView.Unassigned, QueueView.Mine, QueueView.All]);
    expect(initialQueueView(tabs)).toBe(QueueView.Unassigned);
  });

  it('offers only all to a reader who may not claim', () => {
    const tabs = staffQueueTabs(false);
    expect(tabs.map((tab) => tab.value)).toEqual([QueueView.All]);
    expect(initialQueueView(tabs)).toBe(QueueView.All);
    expect(initialQueueView([])).toBe(QueueView.All);
  });

  it('accepts only known views', () => {
    expect(asQueueView(QueueView.Mine)).toBe(QueueView.Mine);
    expect(asQueueView('team')).toBeNull();
    expect(asQueueView(null)).toBeNull();
  });
});

describe('status display', () => {
  it('has a catalog key for every status and builds the audience wording keys', () => {
    expect(ALL_SERVICE_REQUEST_STATUSES.map((status) => SERVICE_REQUEST_STATUS_KEYS[status]).every(Boolean)).toBe(true);
    expect(serviceRequestStatusWording('under-review', WorkflowAudience.Staff)).toBe('serviceRequests.status.under-review.staff');
    expect(serviceRequestStatusWording('approved', WorkflowAudience.Admin)).toBe('serviceRequests.status.approved.admin');
  });

  it('filters the queue only by the statuses a queued request can have', () => {
    expect(STAFF_QUEUE_STATUSES).not.toContain(ServiceRequestStatus.Draft);
    expect(STAFF_QUEUE_STATUSES).toContain(ServiceRequestStatus.Resubmitted);
  });

  it('gives terminal outcomes their tones and an unknown status a neutral one', () => {
    expect(serviceRequestStatusTone(ServiceRequestStatus.Approved)).toBe('success');
    expect(serviceRequestStatusTone(ServiceRequestStatus.Rejected)).toBe('error');
    expect(serviceRequestStatusTone(ServiceRequestStatus.UnderReview)).toBe('progress');
    expect(serviceRequestStatusTone(99)).toBe('neutral');
  });

  it('makes approve the primary step, reject destructive and an unknown action neutral', () => {
    expect(serviceRequestActionTone(ServiceRequestActions.Approve)).toBe(ActionTone.Primary);
    expect(serviceRequestActionTone(ServiceRequestActions.Reject)).toBe(ActionTone.Destructive);
    expect(serviceRequestActionTone('escalate')).toBe(ActionTone.Neutral);
    expect(serviceRequestActionLabelKey(ServiceRequestActions.Return)).toBe('serviceRequests.action.return');
  });
});

describe('record card', () => {
  it('shows the number left to right, the requester and the submission time', () => {
    const card = serviceRequestRecordCard(staffRecord(), 'en');
    expect(card.titleKey).toBe(RecordKeys.Card);
    expect(card.fields.map((field) => field.labelKey)).toEqual([RecordKeys.Number, RecordKeys.Owner, RecordKeys.SubmittedAt]);
    expect(card.fields[0]).toMatchObject({ value: 'SR-2026-001', valueDir: 'ltr' });
    expect(card.fields[1].value).toBe('Requester One');
    expect(String(card.fields[2].value)).toContain('2026');
  });

  it('adds the latest reason only when there is one', () => {
    const card = serviceRequestRecordCard(staffRecord({ lastReason: 'Attach the signed form.' }), 'en');
    expect(card.fields.at(-1)).toMatchObject({ labelKey: RecordKeys.LastReason, value: 'Attach the signed form.', wide: true });
    expect(serviceRequestRecordCard(staffRecord({ lastReason: '  ' }), 'en').fields).toHaveLength(3);
  });

  it('leaves the submission time empty for a request never submitted', () => {
    expect(serviceRequestRecordCard(staffRecord({ submittedAt: null, number: null }), 'en').fields[2].value).toBeNull();
  });

  it('puts the record card before the request\'s own cards', () => {
    const cards = serviceRequestDetailCards(staffRecord(), serviceRequestDetails(), 'en');
    expect(cards.map((card) => card.titleKey)).toEqual([RecordKeys.Card, 'serviceRequests.cards.request', 'serviceRequests.cards.details']);
  });
});

describe('route id', () => {
  it('accepts a PublicId-shaped id and refuses anything else', () => {
    expect(serviceRequestIdFromRoute(TEST_SERVICE_REQUEST_ID)).toBe(TEST_SERVICE_REQUEST_ID);
    expect(serviceRequestIdFromRoute('42')).toBeNull();
    expect(serviceRequestIdFromRoute(undefined)).toBeNull();
    expect(SERVICE_REQUEST_NOT_FOUND.code).toBe('RECORDS_NOT_FOUND');
  });
});
