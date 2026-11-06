import { TestBed } from '@angular/core/testing';
import { HttpStatus } from '@codelab/ng-security';
import { Subject, of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { QueueView, QueuesClient, AssignmentsErrorCodes, type RecordPage, type WorkQueueItem } from '../../../shared/records';
import { SERVICE_REQUEST_RECORD_TYPE, ServiceRequestStatus } from '../data';
import { ServiceRequestQueueStore } from './service-request-queue.store';
import { ServiceRequestsStaffPermissionKeys } from './service-requests-staff.permissions';
import { ServiceRequestsStaffTranslationKeys } from './service-requests-staff-translation-keys';
import { AssignmentHolder } from './staff-decisions';
import { OTHER_USER_ID, assignee, flush, grant, problem, queueItem, staffTestProviders } from './service-requests-staff.testing';

type FakeQueues = Record<'list' | 'claim' | 'release' | 'assign', ReturnType<typeof vi.fn>>;

const PAGE_SIZE = 20;

function page(items: readonly WorkQueueItem[], total = items.length): RecordPage<WorkQueueItem> {
  return { items, page: 1, pageSize: PAGE_SIZE, total };
}

describe('ServiceRequestQueueStore', () => {
  let queues: FakeQueues;

  function create(...keys: Parameters<typeof grant>): ServiceRequestQueueStore {
    TestBed.configureTestingModule({
      providers: [...staffTestProviders(), ServiceRequestQueueStore, { provide: QueuesClient, useValue: queues }],
    });
    grant(...keys);
    return TestBed.inject(ServiceRequestQueueStore);
  }

  const lastQuery = () => queues.list.mock.calls.at(-1)?.[1];

  beforeEach(() => {
    queues = {
      list: vi.fn().mockReturnValue(of(page([queueItem()], 41))),
      claim: vi.fn().mockReturnValue(of(undefined)),
      release: vi.fn().mockReturnValue(of(undefined)),
      assign: vi.fn(),
    };
  });

  it('opens a worker on the unassigned view, first page, no filter', async () => {
    const store = create(ServiceRequestsStaffPermissionKeys.Queue, ServiceRequestsStaffPermissionKeys.Work);
    await flush();
    expect(queues.list).toHaveBeenCalledTimes(1);
    expect(queues.list).toHaveBeenCalledWith(SERVICE_REQUEST_RECORD_TYPE, {
      view: QueueView.Unassigned,
      status: null,
      number: null,
      page: 1,
      pageSize: store.pageSize,
    });
    expect(store.rows()).toHaveLength(1);
    expect(store.total()).toBe(41);
    expect(store.loading()).toBe(false);
  });

  it('opens a reader who may not claim on the all view, with only that tab', async () => {
    const store = create(ServiceRequestsStaffPermissionKeys.Queue);
    await flush();
    expect(store.tabs().map((tab) => tab.value)).toEqual([QueueView.All]);
    expect(lastQuery()?.view).toBe(QueueView.All);
  });

  it('goes back to page 1 on a status filter, a number search and a view change', async () => {
    const store = create(ServiceRequestsStaffPermissionKeys.Queue, ServiceRequestsStaffPermissionKeys.Work);
    await flush();

    store.setPage(3);
    await flush();
    expect(lastQuery()?.page).toBe(3);

    store.setStatus(ServiceRequestStatus.UnderReview);
    await flush();
    expect(lastQuery()).toMatchObject({ status: ServiceRequestStatus.UnderReview, page: 1 });

    store.setPage(2);
    store.setNumber('  SR-2026  ');
    await flush();
    expect(lastQuery()).toMatchObject({ number: 'SR-2026', page: 1, status: ServiceRequestStatus.UnderReview });

    store.setPage(2);
    store.setView(QueueView.Mine);
    await flush();
    expect(lastQuery()).toMatchObject({ view: QueueView.Mine, page: 1, number: 'SR-2026' });
  });

  it('ignores an unknown view, a repeated filter and a page below 1', async () => {
    const store = create(ServiceRequestsStaffPermissionKeys.Queue, ServiceRequestsStaffPermissionKeys.Work);
    await flush();
    const calls = queues.list.mock.calls.length;
    store.setView('team');
    store.setStatus(null);
    store.setNumber('   ');
    await flush();
    expect(queues.list.mock.calls.length).toBe(calls);
    store.setPage(0);
    expect(store.page()).toBe(1);
  });

  it('keeps only the latest answer when an older one arrives late', async () => {
    const slow = new Subject<RecordPage<WorkQueueItem>>();
    queues.list.mockReturnValueOnce(slow);
    const store = create(ServiceRequestsStaffPermissionKeys.Queue, ServiceRequestsStaffPermissionKeys.Work);
    await flush();
    queues.list.mockReturnValue(of(page([queueItem({ number: 'SR-2026-002' })])));
    store.setPage(2);
    await flush();
    slow.next(page([queueItem({ number: 'SR-2026-OLD' })]));
    slow.complete();
    await flush();
    expect(store.rows().map((row) => row.number)).toEqual(['SR-2026-002']);
  });

  it('clears the rows and keeps the refusal when the list fails', async () => {
    queues.list.mockReturnValue(throwError(() => problem(HttpStatus.forbidden, 'FORBIDDEN')));
    const store = create(ServiceRequestsStaffPermissionKeys.Queue);
    await flush();
    expect(store.rows()).toEqual([]);
    expect(store.total()).toBe(0);
    expect(store.error()?.code).toBe('FORBIDDEN');
  });

  it('claims a row, says so and reloads the queue', async () => {
    const store = create(ServiceRequestsStaffPermissionKeys.Queue, ServiceRequestsStaffPermissionKeys.Work);
    await flush();
    const calls = queues.list.mock.calls.length;
    await store.claim(queueItem());
    expect(queues.claim).toHaveBeenCalledWith(SERVICE_REQUEST_RECORD_TYPE, queueItem().id);
    expect(store.notice()).toEqual({ key: ServiceRequestsStaffTranslationKeys.Queue.Claimed, number: 'SR-2026-001' });
    expect(queues.list.mock.calls.length).toBe(calls + 1);
    expect(store.workingRowId()).toBeNull();
  });

  it('shows a claim someone else won and reloads the queue', async () => {
    queues.claim.mockReturnValue(throwError(() => problem(HttpStatus.conflict, AssignmentsErrorCodes.AlreadyClaimed)));
    const store = create(ServiceRequestsStaffPermissionKeys.Queue, ServiceRequestsStaffPermissionKeys.Work);
    await flush();
    const calls = queues.list.mock.calls.length;
    await store.claim(queueItem());
    expect(store.actionError()?.code).toBe(AssignmentsErrorCodes.AlreadyClaimed);
    expect(store.notice()).toBeNull();
    expect(queues.list.mock.calls.length).toBe(calls + 1);
  });

  it('releases a row the user holds', async () => {
    const store = create(ServiceRequestsStaffPermissionKeys.Queue, ServiceRequestsStaffPermissionKeys.Work);
    await flush();
    await store.release(queueItem({ assignee: assignee() }));
    expect(queues.release).toHaveBeenCalledWith(SERVICE_REQUEST_RECORD_TYPE, queueItem().id);
    expect(store.notice()?.key).toBe(ServiceRequestsStaffTranslationKeys.Queue.Released);
  });

  it('tells who holds a row', () => {
    const store = create(ServiceRequestsStaffPermissionKeys.Queue);
    expect(store.holderOf(queueItem())).toBe(AssignmentHolder.Nobody);
    expect(store.holderOf(queueItem({ assignee: assignee() }))).toBe(AssignmentHolder.CurrentUser);
    expect(store.holderOf(queueItem({ assignee: assignee({ userId: OTHER_USER_ID }) }))).toBe(AssignmentHolder.SomeoneElse);
  });
});
