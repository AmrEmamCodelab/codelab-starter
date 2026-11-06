import { TestBed } from '@angular/core/testing';
import { HttpStatus } from '@codelab/ng-security';
import { Subject, of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import type { RecordDetail } from '../../../../shared/records';
import { ServiceRequestActions, ServiceRequestStatus } from '../../data';
import {
  TEST_SERVICE_REQUEST_ID,
  aRecord,
  aStep,
  clientProviders,
  failing,
  fakeRecordsClient,
  fakeServiceRequestsClient,
  httpRefusal,
  requesterTestProviders,
  serviceRequestDetails,
} from '../requester.testing';
import { ServiceRequestDetailStore } from './service-request-detail.store';

function setUp(records = fakeRecordsClient(), requests = fakeServiceRequestsClient()) {
  TestBed.configureTestingModule({
    providers: [...requesterTestProviders(), ...clientProviders({ records, requests }), ServiceRequestDetailStore],
  });
  return { store: TestBed.inject(ServiceRequestDetailStore), records, requests };
}

describe('ServiceRequestDetailStore', () => {
  it('loads the record, its timeline and the request fields together', () => {
    const { store, records, requests } = setUp();
    store.load(TEST_SERVICE_REQUEST_ID);
    expect(records.detail).toHaveBeenCalledWith('service-request', TEST_SERVICE_REQUEST_ID);
    expect(records.timeline).toHaveBeenCalledWith('service-request', TEST_SERVICE_REQUEST_ID);
    expect(requests.get).toHaveBeenCalledWith(TEST_SERVICE_REQUEST_ID);
    expect(store.view()).toEqual({ record: aRecord(), timeline: [aStep()], details: serviceRequestDetails() });
    expect(store.loading()).toBe(false);
  });

  it('does not ask a draft for its fields', () => {
    const { store, requests } = setUp(fakeRecordsClient({ detail: vi.fn(() => of(aRecord(ServiceRequestStatus.Draft))) }));
    store.load(TEST_SERVICE_REQUEST_ID);
    expect(requests.get).not.toHaveBeenCalled();
    expect(store.view()?.details).toBeNull();
  });

  it('keeps a failed load as the page error and loads again on reload', () => {
    const detail = vi.fn().mockReturnValueOnce(failing(httpRefusal(HttpStatus.notFound, 'RECORDS_NOT_FOUND'))).mockReturnValue(of(aRecord()));
    const { store } = setUp(fakeRecordsClient({ detail }));
    store.load(TEST_SERVICE_REQUEST_ID);
    expect(store.error()?.code).toBe('RECORDS_NOT_FOUND');
    store.reload();
    expect(store.error()).toBeNull();
    expect(store.view()?.record).toEqual(aRecord());
  });

  it('sends a decision, is busy meanwhile, and replaces the record and the timeline', () => {
    const answer = new Subject<RecordDetail>();
    const cancelled = aRecord(ServiceRequestStatus.Cancelled);
    const records = fakeRecordsClient({ transition: vi.fn(() => answer) });
    const { store } = setUp(records);
    store.load(TEST_SERVICE_REQUEST_ID);
    records.timeline.mockReturnValue(of([aStep(), aStep({ actionKey: ServiceRequestActions.Cancel })]));
    store.decide({ actionKey: ServiceRequestActions.Cancel, reason: 'No longer needed' });
    expect(store.busy()).toBe(true);
    expect(records.transition).toHaveBeenCalledWith('service-request', TEST_SERVICE_REQUEST_ID, {
      action: ServiceRequestActions.Cancel,
      reason: 'No longer needed',
    });
    answer.next(cancelled);
    answer.complete();
    expect(store.busy()).toBe(false);
    expect(store.view()?.record).toBe(cancelled);
    expect(store.view()?.timeline).toHaveLength(2);
    expect(store.view()?.details).toEqual(serviceRequestDetails());
  });

  it('keeps a refused decision for the dialog and leaves the record as it was', () => {
    const refusal = httpRefusal(HttpStatus.conflict, 'WORKFLOW_ACTION_NOT_ALLOWED');
    const { store } = setUp(fakeRecordsClient({ transition: vi.fn(() => failing(refusal)) }));
    store.load(TEST_SERVICE_REQUEST_ID);
    store.decide({ actionKey: ServiceRequestActions.Cancel, reason: null });
    expect(store.busy()).toBe(false);
    expect(store.actionError()?.code).toBe('WORKFLOW_ACTION_NOT_ALLOWED');
    expect(store.view()?.record).toEqual(aRecord());
  });

  it('ignores a decision before the request is loaded', () => {
    const { store, records } = setUp();
    store.decide({ actionKey: ServiceRequestActions.Cancel, reason: null });
    expect(records.transition).not.toHaveBeenCalled();
  });
});
