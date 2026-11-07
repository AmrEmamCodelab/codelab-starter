import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { DgaPageFeedbackKind } from '@codelab/ng-dga';
import { HttpStatus } from '@codelab/ng-security';
import { Subject, of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { RecordAttachmentsComponent } from '../../../shared/record-attachments/record-attachments.component';
import { RecordsClient, TransitionSide, type OversightListItem, type RecordPage } from '../../../shared/records';
import { SERVICE_REQUEST_RECORD_TYPE, ServiceRequestActions, ServiceRequestStatus, ServiceRequestsClient } from '../data';
import { TEST_ATTACHMENTS_RECORD_ID, TEST_SERVICE_REQUEST_ID, serviceRequestDetails } from '../data/service-requests.testing';
import {
  RecordAttachmentsStubComponent,
  SECOND_REQUEST_ID,
  allByTestId,
  flush,
  grant,
  oversightItem,
  problem,
  recordIn,
  settle,
  staffTestProviders,
  timelineEntry,
} from '../staff/service-requests-staff.testing';
import { adminHistory, adminHistoryTone } from './admin-history';
import { ServiceRequestAdminDetailPageComponent } from './service-request-admin-detail-page.component';
import { ServiceRequestOversightPageComponent } from './service-request-oversight-page.component';
import { ServiceRequestOversightStore } from './service-request-oversight.store';
import { ServiceRequestsAdminPermissionKeys } from './service-requests-admin.permissions';
import { ServiceRequestsAdminTranslationKeys } from './service-requests-admin-translation-keys';

const Keys = ServiceRequestsAdminTranslationKeys;
const PAGE_SIZE = 20;
const MANY = 30;

function page(items: readonly OversightListItem[], total = items.length): RecordPage<OversightListItem> {
  return { items, page: 1, pageSize: PAGE_SIZE, total };
}

const rows = [
  oversightItem(),
  oversightItem({ id: SECOND_REQUEST_ID, number: null, status: ServiceRequestStatus.Draft, statusKey: 'draft', submittedAt: null }),
];

/** Every way a page could change a request: none may exist on the read-only screens. */
const WRITE_METHODS = ['transition'] as const;

describe('admin oversight', () => {
  let records: Record<'oversight' | 'detail' | 'timeline' | 'transition' | 'mine', ReturnType<typeof vi.fn>>;

  beforeEach(() => {
    records = {
      oversight: vi.fn().mockReturnValue(of(page(rows, MANY))),
      detail: vi.fn().mockReturnValue(of(recordIn(ServiceRequestStatus.Approved, 'approved', { lastReason: null }))),
      timeline: vi.fn().mockReturnValue(of([
        timelineEntry(),
        timelineEntry({ at: '2026-11-04T08:00:00Z', actorDisplayName: 'Staff One', side: TransitionSide.Staff, actionKey: ServiceRequestActions.Return, toStatus: ServiceRequestStatus.Returned, toStatusKey: 'returned', reason: 'Attach the signed form.' }),
      ])),
      transition: vi.fn(),
      mine: vi.fn(),
    };
    TestBed.configureTestingModule({
      imports: [ServiceRequestOversightPageComponent, ServiceRequestAdminDetailPageComponent],
      providers: [
        ...staffTestProviders(),
        ServiceRequestOversightStore,
        { provide: RecordsClient, useValue: records },
        { provide: ServiceRequestsClient, useValue: { get: vi.fn().mockReturnValue(of(serviceRequestDetails())) } },
      ],
    });
    TestBed.overrideComponent(ServiceRequestAdminDetailPageComponent, {
      remove: { imports: [RecordAttachmentsComponent] },
      add: { imports: [RecordAttachmentsStubComponent] },
    });
  });

  describe('ServiceRequestOversightStore', () => {
    let store: ServiceRequestOversightStore;
    const lastQuery = () => records.oversight.mock.calls.at(-1)?.[1];

    beforeEach(() => {
      store = TestBed.inject(ServiceRequestOversightStore);
    });

    it('loads the first page with no filter', async () => {
      await flush();
      expect(records.oversight).toHaveBeenCalledWith(SERVICE_REQUEST_RECORD_TYPE, { status: null, number: null, page: 1, pageSize: store.pageSize });
      expect(store.total()).toBe(MANY);
    });

    it('goes back to page 1 on a status filter or a number search', async () => {
      await flush();
      store.setPage(2);
      await flush();
      expect(lastQuery()?.page).toBe(2);
      store.setStatus(ServiceRequestStatus.Rejected);
      await flush();
      expect(lastQuery()).toMatchObject({ status: ServiceRequestStatus.Rejected, page: 1 });
      store.setPage(2);
      store.setNumber(' SR-2026-00 ');
      await flush();
      expect(lastQuery()).toMatchObject({ number: 'SR-2026-00', page: 1 });
    });

    it('keeps only the latest answer and clears the rows on a failure', async () => {
      const slow = new Subject<RecordPage<OversightListItem>>();
      records.oversight.mockReturnValueOnce(slow);
      await flush();
      records.oversight.mockReturnValue(throwError(() => problem(HttpStatus.forbidden, 'FORBIDDEN')));
      store.setPage(2);
      await flush();
      slow.next(page(rows));
      slow.complete();
      await flush();
      expect(store.rows()).toEqual([]);
      expect(store.error()?.code).toBe('FORBIDDEN');
    });

    it('has no way to change a request', () => {
      expect(Object.getOwnPropertyNames(Object.getPrototypeOf(store)).sort()).toEqual(
        ['constructor', 'load', 'reload', 'setNumber', 'setPage', 'setStatus'].sort());
    });
  });

  describe('ServiceRequestOversightPageComponent', () => {
    let fixture: ComponentFixture<ServiceRequestOversightPageComponent>;
    let host: HTMLElement;

    beforeEach(async () => {
      grant(ServiceRequestsAdminPermissionKeys.Oversight);
      fixture = TestBed.createComponent(ServiceRequestOversightPageComponent);
      host = fixture.nativeElement as HTMLElement;
      fixture.detectChanges();
      await settle(fixture);
    });

    it('lists every request in the admin wording, with links to the read-only detail', () => {
      const lines = host.querySelectorAll('tbody tr');
      expect(lines).toHaveLength(2);
      expect(lines[0].textContent).toContain('serviceRequests.status.under-review.admin');
      expect(lines[0].textContent).toContain('Requester One');
      expect(lines[1].textContent).toContain(Keys.List.NoNumber);
      expect(lines[1].textContent).toContain(Keys.List.NotSubmitted);
      expect(allByTestId(host, 'oversight-open')[0].getAttribute('href')).toBe(`/admin/requests/${TEST_SERVICE_REQUEST_ID}`);
      expect(allByTestId(host, 'oversight-read-only')).toHaveLength(1);
    });

    it('has no mutating control: no button in the rows, no toolbar action, no decision', () => {
      expect(host.querySelectorAll('tbody button, tbody dga-button')).toHaveLength(0);
      expect(host.querySelectorAll('[data-action], app-decision-panel, app-decision-dialog')).toHaveLength(0);
      const buttons = [...host.querySelectorAll('button')].filter((button) => !button.closest('nav'));
      expect(buttons).toHaveLength(0);
      for (const method of WRITE_METHODS) {
        expect(records[method]).not.toHaveBeenCalled();
      }
    });

    it('filters by status', async () => {
      const select = host.querySelector('[data-testid="oversight-status-filter"] select') as HTMLSelectElement;
      select.value = '6';
      select.dispatchEvent(new Event('change'));
      await settle(fixture);
      expect(records.oversight.mock.calls.at(-1)?.[1]).toMatchObject({ status: ServiceRequestStatus.Rejected, page: 1 });
    });
  });

  describe('ServiceRequestAdminDetailPageComponent', () => {
    let fixture: ComponentFixture<ServiceRequestAdminDetailPageComponent>;
    let host: HTMLElement;

    async function render(id: string = TEST_SERVICE_REQUEST_ID): Promise<void> {
      grant(ServiceRequestsAdminPermissionKeys.Oversight);
      fixture = TestBed.createComponent(ServiceRequestAdminDetailPageComponent);
      fixture.componentRef.setInput('id', id);
      host = fixture.nativeElement as HTMLElement;
      fixture.detectChanges();
      await settle(fixture);
    }

    it('shows the request read-only: admin wording, cards, attachments for download and the history with actors and reasons', async () => {
      await render();
      expect(host.querySelector('.requester-detail__status')?.textContent).toContain('serviceRequests.status.approved.admin');
      expect(host.textContent).toContain('SR-2026-001');
      expect(host.textContent).toContain('Experience certificate');
      expect(host.querySelector('.attachments-stub')?.textContent).toBe(`${SERVICE_REQUEST_RECORD_TYPE}|true`);
      expect(host.textContent).toContain('Staff One');
      expect(host.textContent).toContain('Attach the signed form.');
      expect(host.textContent).toContain('serviceRequests.status.returned.admin');
    });

    it('gives the attachments widget the attachments id, never the request id', async () => {
      await render();
      const widget = fixture.debugElement.query(By.directive(RecordAttachmentsStubComponent)).componentInstance as RecordAttachmentsStubComponent;
      expect(widget.recordId()).toBe(TEST_ATTACHMENTS_RECORD_ID);
      expect(widget.recordId()).not.toBe(TEST_SERVICE_REQUEST_ID);
    });

    it('shows page feedback instead of the widget when the record carries no attachments id', async () => {
      records.detail.mockReturnValue(of(recordIn(ServiceRequestStatus.Approved, 'approved', { attachmentsRecordId: '' })));
      await render();
      expect(fixture.debugElement.query(By.directive(RecordAttachmentsStubComponent))).toBeNull();
      const feedback = allByTestId(host, 'admin-detail-attachments-failed')[0]?.querySelector('.dga-page-feedback');
      expect(feedback?.getAttribute('data-kind')).toBe(DgaPageFeedbackKind.Error);
      expect(feedback?.textContent).toContain('errors.INTERNAL_ERROR');
    });

    it('renders no decision panel, no action group and no button, even when the server sent allowed actions', async () => {
      records.detail.mockReturnValue(of(recordIn(ServiceRequestStatus.UnderReview, 'under-review')));
      await render();
      expect(host.querySelectorAll('app-decision-panel, app-decision-dialog, app-staff-detail-pattern, [data-action]')).toHaveLength(0);
      expect(host.querySelector('.requester-detail__actions')).toBeNull();
      expect(host.querySelectorAll('button, dga-button')).toHaveLength(0);
      expect(records.transition).not.toHaveBeenCalled();
    });

    it('shows "not found" for an id that is not a PublicId, with a retry', async () => {
      await render('bad');
      expect(records.detail).not.toHaveBeenCalled();
      expect(host.querySelector('[role="alert"]')?.textContent).toContain('errors.RECORDS_NOT_FOUND');
    });
  });

  describe('admin history', () => {
    it('maps each step to the admin wording with its actor and reason, leaving a blank reason out', () => {
      const history = adminHistory([
        timelineEntry(),
        timelineEntry({ toStatusKey: 'rejected', actorDisplayName: 'Staff One', reason: 'Out of scope.' }),
        timelineEntry({ toStatusKey: 'returned', reason: '  ' }),
      ]);
      expect(history[0]).toEqual({ statusKey: 'serviceRequests.status.submitted.admin', at: '2026-11-02T09:00:00Z', actorLabel: 'Requester One' });
      expect(history[1]).toMatchObject({ statusKey: 'serviceRequests.status.rejected.admin', actorLabel: 'Staff One', reasonText: 'Out of scope.' });
      expect(history[2].reasonText).toBeUndefined();
    });

    it('tones the tracker by the admin wording key', () => {
      expect(adminHistoryTone('serviceRequests.status.approved.admin')).toBe('success');
      expect(adminHistoryTone('serviceRequests.status.rejected.admin')).toBe('error');
      expect(adminHistoryTone('something.else')).toBe('neutral');
    });
  });
});
