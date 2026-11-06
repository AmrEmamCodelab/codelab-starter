import { TestBed } from '@angular/core/testing';
import { HttpStatus } from '@codelab/ng-security';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { QueuesClient, RecordsClient, WorkflowErrorCodes } from '../../../shared/records';
import { SERVICE_REQUEST_RECORD_TYPE, ServiceRequestActions, ServiceRequestStatus, ServiceRequestsClient } from '../data';
import { TEST_SERVICE_REQUEST_ID, serviceRequestDetails } from '../data/service-requests.testing';
import { ServiceRequestStaffDetailStore } from './service-request-staff-detail.store';
import {
  OTHER_USER_ID,
  assignee,
  problem,
  recordIn,
  staffRecord,
  staffTestProviders,
  timelineEntry,
} from './service-requests-staff.testing';

type Fake<K extends string> = Record<K, ReturnType<typeof vi.fn>>;

describe('ServiceRequestStaffDetailStore', () => {
  let records: Fake<'detail' | 'timeline' | 'transition'>;
  let queues: Fake<'claim' | 'release'>;
  let requests: Fake<'get'>;
  let store: ServiceRequestStaffDetailStore;

  beforeEach(() => {
    records = {
      detail: vi.fn().mockReturnValue(of(staffRecord())),
      timeline: vi.fn().mockReturnValue(of([timelineEntry()])),
      transition: vi.fn().mockReturnValue(of(recordIn(ServiceRequestStatus.Approved, 'approved'))),
    };
    queues = { claim: vi.fn().mockReturnValue(of(undefined)), release: vi.fn().mockReturnValue(of(undefined)) };
    requests = { get: vi.fn().mockReturnValue(of(serviceRequestDetails())) };
    TestBed.configureTestingModule({
      providers: [
        ...staffTestProviders(),
        ServiceRequestStaffDetailStore,
        { provide: RecordsClient, useValue: records },
        { provide: QueuesClient, useValue: queues },
        { provide: ServiceRequestsClient, useValue: requests },
      ],
    });
    store = TestBed.inject(ServiceRequestStaffDetailStore);
  });

  it('loads the record, the request and the timeline', async () => {
    await store.open(TEST_SERVICE_REQUEST_ID);
    expect(records.detail).toHaveBeenCalledWith(SERVICE_REQUEST_RECORD_TYPE, TEST_SERVICE_REQUEST_ID);
    expect(requests.get).toHaveBeenCalledWith(TEST_SERVICE_REQUEST_ID);
    expect(store.details()?.title).toBe('Experience certificate');
    expect(store.timeline()).toHaveLength(1);
    expect(store.decisionButtons().map((button) => button.code)).toEqual([
      ServiceRequestActions.Approve,
      ServiceRequestActions.Return,
      ServiceRequestActions.Reject,
    ]);
  });

  it('shows "not found" for an id that is not a PublicId, without calling the API', async () => {
    await store.open('not-an-id');
    expect(records.detail).not.toHaveBeenCalled();
    expect(store.loadError()?.code).toBe('RECORDS_NOT_FOUND');
  });

  it('keeps the refusal of a failed load', async () => {
    records.detail.mockReturnValue(throwError(() => problem(HttpStatus.notFound, 'RECORDS_NOT_FOUND')));
    await store.open(TEST_SERVICE_REQUEST_ID);
    expect(store.detail()).toBeNull();
    expect(store.loadError()?.code).toBe('RECORDS_NOT_FOUND');
  });

  it('sends an action without a reason at once, then shows the answer and the new timeline', async () => {
    await store.open(TEST_SERVICE_REQUEST_ID);
    records.timeline.mockReturnValue(of([timelineEntry(), timelineEntry({ toStatusKey: 'approved' })]));
    await store.choose(store.decisionButtons()[0]);
    expect(records.transition).toHaveBeenCalledWith(SERVICE_REQUEST_RECORD_TYPE, TEST_SERVICE_REQUEST_ID, {
      action: ServiceRequestActions.Approve,
      reason: null,
    });
    expect(store.detail()?.isTerminal).toBe(true);
    expect(store.decisionButtons()).toEqual([]);
    expect(store.timeline()).toHaveLength(2);
    expect(store.decided()).toBe('serviceRequests.action.approve');
  });

  it('asks for the reason of a Required action and sends it on confirm', async () => {
    records.transition.mockReturnValue(of(recordIn(ServiceRequestStatus.Returned, 'returned')));
    await store.open(TEST_SERVICE_REQUEST_ID);
    const returnButton = store.decisionButtons().find((button) => button.code === ServiceRequestActions.Return);
    await store.choose(returnButton!);
    expect(records.transition).not.toHaveBeenCalled();
    expect(store.pending()?.actionKey).toBe(ServiceRequestActions.Return);

    await store.confirmReason('Attach the signed form.');
    expect(records.transition).toHaveBeenCalledWith(SERVICE_REQUEST_RECORD_TYPE, TEST_SERVICE_REQUEST_ID, {
      action: ServiceRequestActions.Return,
      reason: 'Attach the signed form.',
    });
    expect(store.pending()).toBeNull();
  });

  it('keeps the dialog open with the refusal when the server refuses the reason', async () => {
    records.transition.mockReturnValue(throwError(() => problem(HttpStatus.badRequest, WorkflowErrorCodes.ReasonRequired)));
    await store.open(TEST_SERVICE_REQUEST_ID);
    await store.choose(store.decisionButtons()[2]);
    await store.confirmReason('x');
    expect(store.pending()?.actionKey).toBe(ServiceRequestActions.Reject);
    expect(store.decideError()?.code).toBe(WorkflowErrorCodes.ReasonRequired);
    store.cancelReason();
    expect(store.pending()).toBeNull();
    expect(store.decideError()).toBeNull();
  });

  it('reloads the record when it moved on before the decision', async () => {
    await store.open(TEST_SERVICE_REQUEST_ID);
    records.transition.mockReturnValue(throwError(() => problem(HttpStatus.conflict, WorkflowErrorCodes.ActionNotAllowed)));
    records.detail.mockReturnValue(of(recordIn(ServiceRequestStatus.Approved, 'approved')));
    await store.choose(store.decisionButtons()[0]);
    expect(records.detail).toHaveBeenCalledTimes(2);
    expect(store.decisionButtons()).toEqual([]);
    expect(store.decideError()?.code).toBe(WorkflowErrorCodes.ActionNotAllowed);
  });

  it('ignores a button the server no longer allows', async () => {
    await store.open(TEST_SERVICE_REQUEST_ID);
    await store.choose({ code: 'escalate', labelKey: 'x', requiresReason: false, tone: 'neutral' });
    expect(records.transition).not.toHaveBeenCalled();
  });

  it('offers claim on an unassigned open request and reloads it after the claim', async () => {
    records.detail.mockReturnValue(of(staffRecord({ assignee: null, allowedActions: [] })));
    await store.open(TEST_SERVICE_REQUEST_ID);
    expect(store.canClaim()).toBe(true);
    expect(store.canRelease()).toBe(false);
    records.detail.mockReturnValue(of(staffRecord()));
    await store.claim();
    expect(queues.claim).toHaveBeenCalledWith(SERVICE_REQUEST_RECORD_TYPE, TEST_SERVICE_REQUEST_ID);
    expect(store.canRelease()).toBe(true);
    expect(store.decisionButtons()).toHaveLength(3);
  });

  it('offers neither claim nor release on a request someone else holds or a terminal one', async () => {
    records.detail.mockReturnValue(of(staffRecord({ assignee: assignee({ userId: OTHER_USER_ID }) })));
    await store.open(TEST_SERVICE_REQUEST_ID);
    expect(store.canClaim()).toBe(false);
    expect(store.canRelease()).toBe(false);

    records.detail.mockReturnValue(of(recordIn(ServiceRequestStatus.Rejected, 'rejected', { assignee: null })));
    await store.reload();
    expect(store.canClaim()).toBe(false);
  });

  it('keeps the refusal of a failed release and reloads', async () => {
    queues.release.mockReturnValue(throwError(() => problem(HttpStatus.forbidden, 'ASSIGNMENTS_NOT_ASSIGNEE')));
    await store.open(TEST_SERVICE_REQUEST_ID);
    await store.release();
    expect(store.assignError()?.code).toBe('ASSIGNMENTS_NOT_ASSIGNEE');
    expect(records.detail).toHaveBeenCalledTimes(2);
  });
});
