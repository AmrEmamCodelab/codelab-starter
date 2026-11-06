import { DgaAlertVariant } from '@codelab/ng-dga';
import { describe, expect, it } from 'vitest';
import { DecisionTone, TransitionSide } from '../../../shared/records';
import { ServiceRequestActions, ServiceRequestStatus } from '../data';
import {
  SERVICE_REQUEST_STATUS_KEYS,
  attachmentsEditable,
  isDraftStatus,
  requesterActionTone,
  requesterDetailBanner,
  requesterStatusHistory,
  requesterStatusKey,
  requesterTrackerTone,
  serviceRequestActionLabelKey,
  serviceRequestStatusTone,
  serviceRequestStatusWording,
} from './requester-status';
import { ServiceRequestsRequesterTranslationKeys as Keys } from './requester-translation-keys';
import { aRecord, aStep } from './requester.testing';

describe('status wording', () => {
  it('names one kebab-case key per status', () => {
    expect(Object.keys(SERVICE_REQUEST_STATUS_KEYS)).toHaveLength(8);
    expect(SERVICE_REQUEST_STATUS_KEYS[ServiceRequestStatus.UnderReview]).toBe('under-review');
    expect(new Set(Object.values(SERVICE_REQUEST_STATUS_KEYS)).size).toBe(8);
  });

  it('builds the catalog wording keys for the requester', () => {
    expect(requesterStatusKey('returned')).toBe('serviceRequests.status.returned.requester');
    expect(serviceRequestStatusWording('approved', 2)).toBe('serviceRequests.status.approved.staff');
  });

  it('builds the action label keys', () => {
    expect(serviceRequestActionLabelKey(ServiceRequestActions.StartReview)).toBe('serviceRequests.action.start-review');
  });
});

describe('tones', () => {
  it('maps each status key to a tone and an unknown key to neutral', () => {
    expect(serviceRequestStatusTone('approved')).toBe('success');
    expect(serviceRequestStatusTone('rejected')).toBe('error');
    expect(serviceRequestStatusTone('returned')).toBe('warning');
    expect(serviceRequestStatusTone('under-review')).toBe('progress');
    expect(serviceRequestStatusTone('nope')).toBe('neutral');
  });

  it('gives the tracker the same tone from the wording key', () => {
    expect(requesterTrackerTone(requesterStatusKey('approved'))).toBe('success');
    expect(requesterTrackerTone('approved')).toBe('neutral');
  });

  it('makes resubmit the expected step and cancel destructive', () => {
    expect(requesterActionTone(ServiceRequestActions.Resubmit)).toBe(DecisionTone.Primary);
    expect(requesterActionTone(ServiceRequestActions.Cancel)).toBe(DecisionTone.Destructive);
    expect(requesterActionTone('other')).toBe(DecisionTone.Neutral);
  });
});

describe('requesterStatusHistory', () => {
  it('maps each step to the requester wording with its actor and reason', () => {
    const history = requesterStatusHistory([
      aStep(),
      aStep({ side: TransitionSide.Staff, toStatusKey: 'returned', actorDisplayName: 'Staff One', reason: 'Add the page.' }),
    ]);
    expect(history).toEqual([
      { statusKey: 'serviceRequests.status.submitted.requester', at: '2026-11-01T09:00:00Z', actorLabel: 'Requester One', reasonText: undefined },
      { statusKey: 'serviceRequests.status.returned.requester', at: '2026-11-01T09:00:00Z', actorLabel: 'Staff One', reasonText: 'Add the page.' },
    ]);
  });
});

describe('requesterDetailBanner', () => {
  it('shows the return reason as a warning', () => {
    expect(requesterDetailBanner(aRecord(ServiceRequestStatus.Returned, { lastReason: '  Add the page. ' }))).toEqual({
      variant: DgaAlertVariant.Warning,
      titleKey: Keys.Detail.ReturnedTitle,
      reason: 'Add the page.',
    });
  });

  it('shows the rejection reason as an error', () => {
    expect(requesterDetailBanner(aRecord(ServiceRequestStatus.Rejected, { lastReason: 'Out of scope.' }))?.variant).toBe(DgaAlertVariant.Error);
  });

  it('shows nothing without a reason or in another status', () => {
    expect(requesterDetailBanner(aRecord(ServiceRequestStatus.Returned, { lastReason: ' ' }))).toBeNull();
    expect(requesterDetailBanner(aRecord(ServiceRequestStatus.UnderReview, { lastReason: 'x' }))).toBeNull();
  });
});

describe('status rules of the page', () => {
  it('knows a draft', () => {
    expect(isDraftStatus(ServiceRequestStatus.Draft)).toBe(true);
    expect(isDraftStatus(ServiceRequestStatus.Submitted)).toBe(false);
  });

  it('lets the requester change files only while the request is returned', () => {
    expect(attachmentsEditable(aRecord(ServiceRequestStatus.Returned))).toBe(true);
    expect(attachmentsEditable(aRecord(ServiceRequestStatus.Submitted))).toBe(false);
    expect(attachmentsEditable(aRecord(ServiceRequestStatus.Approved))).toBe(false);
  });
});
