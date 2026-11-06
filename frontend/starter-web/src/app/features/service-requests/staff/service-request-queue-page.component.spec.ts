import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { QueueView, QueuesClient, SlaState, type RecordPage, type WorkQueueItem } from '../../../shared/records';
import { ServiceRequestStatus, SERVICE_REQUEST_RECORD_TYPE } from '../data';
import { ServiceRequestQueuePageComponent } from './service-request-queue-page.component';
import { ServiceRequestsStaffPermissionKeys } from './service-requests-staff.permissions';
import { ServiceRequestsStaffTranslationKeys } from './service-requests-staff-translation-keys';
import {
  OTHER_USER_ID,
  SECOND_REQUEST_ID,
  allByTestId,
  assignee,
  grant,
  press,
  queueItem,
  settle,
  staffTestProviders,
} from './service-requests-staff.testing';

const Keys = ServiceRequestsStaffTranslationKeys;
const PAGE_SIZE = 20;
const MANY = 45;

function page(items: readonly WorkQueueItem[], total = items.length): RecordPage<WorkQueueItem> {
  return { items, page: 1, pageSize: PAGE_SIZE, total };
}

describe('ServiceRequestQueuePageComponent', () => {
  let queues: Record<'list' | 'claim' | 'release', ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<ServiceRequestQueuePageComponent>;
  let host: HTMLElement;

  const rows = [
    queueItem(),
    queueItem({
      id: SECOND_REQUEST_ID,
      number: 'SR-2026-002',
      status: ServiceRequestStatus.UnderReview,
      statusKey: 'under-review',
      assignee: assignee(),
      sla: { state: SlaState.Breached, dueAt: '2026-11-05T09:00:00Z' },
    }),
  ];

  async function render(...keys: Parameters<typeof grant>): Promise<void> {
    grant(...keys);
    fixture = TestBed.createComponent(ServiceRequestQueuePageComponent);
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await settle(fixture);
  }

  const lastQuery = () => queues.list.mock.calls.at(-1)?.[1];

  beforeEach(() => {
    queues = {
      list: vi.fn().mockReturnValue(of(page(rows, MANY))),
      claim: vi.fn().mockReturnValue(of(undefined)),
      release: vi.fn().mockReturnValue(of(undefined)),
    };
    TestBed.configureTestingModule({
      imports: [ServiceRequestQueuePageComponent],
      providers: [...staffTestProviders(), { provide: QueuesClient, useValue: queues }],
    });
  });

  it('lists the rows with the staff wording, the assignee, the SLA badge and a link to the detail', async () => {
    await render(ServiceRequestsStaffPermissionKeys.Queue, ServiceRequestsStaffPermissionKeys.Work);
    const lines = host.querySelectorAll('tbody tr');
    expect(lines).toHaveLength(2);
    expect(lines[0].textContent).toContain('serviceRequests.status.submitted.staff');
    expect(lines[1].textContent).toContain('serviceRequests.status.under-review.staff');
    expect(allByTestId(host, 'queue-unassigned')).toHaveLength(1);
    expect(allByTestId(host, 'queue-assignee')[0].textContent).toContain('Staff One');
    expect(host.querySelector('[data-sla-state="3"]')).not.toBeNull();
    expect(allByTestId(host, 'queue-open').map((link) => link.getAttribute('href'))).toEqual([
      `/staff/service-requests/${rows[0].id}`,
      `/staff/service-requests/${rows[1].id}`,
    ]);
    expect(allByTestId(host, 'queue-notifications')[0].getAttribute('href')).toBe('/notifications');
  });

  it('offers claim on an unassigned row and release on the user\'s own row', async () => {
    await render(ServiceRequestsStaffPermissionKeys.Queue, ServiceRequestsStaffPermissionKeys.Work);
    expect(allByTestId(host, 'queue-claim')).toHaveLength(1);
    expect(allByTestId(host, 'queue-release')).toHaveLength(1);
  });

  it('offers neither on a row someone else holds', async () => {
    queues.list.mockReturnValue(of(page([queueItem({ assignee: assignee({ userId: OTHER_USER_ID, displayName: 'Staff Two' }) })])));
    await render(ServiceRequestsStaffPermissionKeys.Queue, ServiceRequestsStaffPermissionKeys.Work);
    expect(allByTestId(host, 'queue-claim')).toHaveLength(0);
    expect(allByTestId(host, 'queue-release')).toHaveLength(0);
  });

  it('hides claim, release and the worker views from a reader without assignments.work', async () => {
    await render(ServiceRequestsStaffPermissionKeys.Queue);
    expect(allByTestId(host, 'queue-claim')).toHaveLength(0);
    expect(allByTestId(host, 'queue-release')).toHaveLength(0);
    expect(lastQuery()?.view).toBe(QueueView.All);
  });

  it('claims a row, shows the notice and reloads the queue', async () => {
    await render(ServiceRequestsStaffPermissionKeys.Queue, ServiceRequestsStaffPermissionKeys.Work);
    const calls = queues.list.mock.calls.length;
    press(allByTestId(host, 'queue-claim')[0]);
    await settle(fixture);
    expect(queues.claim).toHaveBeenCalledWith(SERVICE_REQUEST_RECORD_TYPE, rows[0].id);
    expect(allByTestId(host, 'queue-notice')[0].textContent).toContain(Keys.Queue.Claimed);
    expect(queues.list.mock.calls.length).toBe(calls + 1);
  });

  it('filters by status from the 1st page and switches views from the tabs', async () => {
    await render(ServiceRequestsStaffPermissionKeys.Queue, ServiceRequestsStaffPermissionKeys.Work);
    const select = host.querySelector('[data-testid="queue-status-filter"] select') as HTMLSelectElement;
    select.value = '1';
    select.dispatchEvent(new Event('change'));
    await settle(fixture);
    expect(lastQuery()).toMatchObject({ status: ServiceRequestStatus.UnderReview, page: 1, view: QueueView.Unassigned });

    const tabs = [...host.querySelectorAll<HTMLElement>('[role="tab"]')];
    expect(tabs.map((tab) => tab.textContent?.trim())).toEqual([Keys.Queue.TabUnassigned, Keys.Queue.TabMine, Keys.Queue.TabAll]);
    tabs[1].click();
    await settle(fixture);
    expect(lastQuery()).toMatchObject({ view: QueueView.Mine, page: 1, status: ServiceRequestStatus.UnderReview });
  });

  it('pages through the server total', async () => {
    await render(ServiceRequestsStaffPermissionKeys.Queue, ServiceRequestsStaffPermissionKeys.Work);
    const next = [...host.querySelectorAll<HTMLButtonElement>('nav button')].find((button) => button.textContent?.trim() === '2');
    expect(next).toBeDefined();
    next?.click();
    await settle(fixture);
    expect(lastQuery()?.page).toBe(2);
  });
});
