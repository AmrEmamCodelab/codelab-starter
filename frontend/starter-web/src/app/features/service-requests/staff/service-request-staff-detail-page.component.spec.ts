import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { DgaPageFeedbackKind } from '@codelab/ng-dga';
import { NEVER, of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { RecordAttachmentsComponent } from '../../../shared/record-attachments/record-attachments.component';
import { QueuesClient, RecordsClient, TransitionSide } from '../../../shared/records';
import { SERVICE_REQUEST_RECORD_TYPE, ServiceRequestActions, ServiceRequestStatus, ServiceRequestsClient } from '../data';
import { TEST_ATTACHMENTS_RECORD_ID, TEST_SERVICE_REQUEST_ID, serviceRequestDetails } from '../data/service-requests.testing';
import { ServiceRequestStaffDetailPageComponent } from './service-request-staff-detail-page.component';
import { ServiceRequestsStaffPermissionKeys } from './service-requests-staff.permissions';
import { ServiceRequestsStaffTranslationKeys } from './service-requests-staff-translation-keys';
import {
  RecordAttachmentsStubComponent,
  allByTestId,
  grant,
  press,
  recordIn,
  settle,
  staffRecord,
  staffTestProviders,
  timelineEntry,
} from './service-requests-staff.testing';

const Keys = ServiceRequestsStaffTranslationKeys;

describe('ServiceRequestStaffDetailPageComponent', () => {
  let records: Record<'detail' | 'timeline' | 'transition', ReturnType<typeof vi.fn>>;
  let queues: Record<'claim' | 'release', ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<ServiceRequestStaffDetailPageComponent>;
  let host: HTMLElement;

  async function render(...keys: Parameters<typeof grant>): Promise<void> {
    grant(...keys);
    fixture = TestBed.createComponent(ServiceRequestStaffDetailPageComponent);
    fixture.componentRef.setInput('id', TEST_SERVICE_REQUEST_ID);
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await settle(fixture);
  }

  const decisionButtons = () => [...host.querySelectorAll<HTMLElement>('.staff-detail__actions [data-action]')];
  const decisionCodes = () => decisionButtons().map((button) => button.getAttribute('data-action'));
  const dialog = () => host.querySelector('app-decision-dialog dialog') as HTMLDialogElement | null;
  const dialogOpen = () => dialog()?.hasAttribute('open') ?? false;
  const submitDialog = async () => {
    (host.querySelector('app-decision-dialog form') as HTMLFormElement).dispatchEvent(new Event('submit', { cancelable: true }));
    await settle(fixture);
  };
  const typeReason = async (text: string) => {
    const textarea = host.querySelector('app-decision-dialog textarea') as HTMLTextAreaElement;
    textarea.value = text;
    textarea.dispatchEvent(new Event('input'));
    await settle(fixture);
  };
  const pressDecision = async (code: string) => {
    press(decisionButtons().find((button) => button.getAttribute('data-action') === code));
    await settle(fixture);
  };

  beforeEach(() => {
    records = {
      detail: vi.fn().mockReturnValue(of(staffRecord())),
      timeline: vi.fn().mockReturnValue(of([
        timelineEntry(),
        timelineEntry({ at: '2026-11-03T08:00:00Z', actorDisplayName: 'Staff One', side: TransitionSide.Staff, actionKey: ServiceRequestActions.StartReview, toStatus: ServiceRequestStatus.UnderReview, toStatusKey: 'under-review' }),
      ])),
      transition: vi.fn(),
    };
    queues = { claim: vi.fn().mockReturnValue(of(undefined)), release: vi.fn().mockReturnValue(of(undefined)) };
    TestBed.configureTestingModule({
      imports: [ServiceRequestStaffDetailPageComponent],
      providers: [
        ...staffTestProviders(),
        { provide: RecordsClient, useValue: records },
        { provide: QueuesClient, useValue: queues },
        { provide: ServiceRequestsClient, useValue: { get: vi.fn().mockReturnValue(of(serviceRequestDetails())) } },
      ],
    });
    TestBed.overrideComponent(ServiceRequestStaffDetailPageComponent, {
      remove: { imports: [RecordAttachmentsComponent] },
      add: { imports: [RecordAttachmentsStubComponent] },
    });
  });

  it('shows the staff wording, the SLA badge, the cards, read-only attachments and the timeline with actors', async () => {
    await render(ServiceRequestsStaffPermissionKeys.Queue, ServiceRequestsStaffPermissionKeys.Work);
    expect(host.querySelector('h1')?.textContent).toContain(Keys.Detail.Title);
    expect(allByTestId(host, 'detail-status')[0].textContent).toContain('serviceRequests.status.under-review.staff');
    expect(host.querySelector('app-sla-badge [data-sla-state="1"]')).not.toBeNull();
    expect(host.textContent).toContain('Experience certificate');
    expect(host.querySelector('.attachments-stub')?.textContent).toBe(`${SERVICE_REQUEST_RECORD_TYPE}|true`);
    const steps = host.querySelectorAll('app-record-timeline li');
    expect(steps).toHaveLength(2);
    expect(steps[0].textContent).toContain('Staff One');
    expect(steps[0].textContent).toContain('serviceRequests.action.start-review');
    expect(allByTestId(host, 'detail-assignee')[0].textContent).toContain('Staff One');
  });

  it('gives the attachments widget the attachments id, never the request id', async () => {
    await render(ServiceRequestsStaffPermissionKeys.Queue);
    const widget = fixture.debugElement.query(By.directive(RecordAttachmentsStubComponent)).componentInstance as RecordAttachmentsStubComponent;
    expect(widget.recordId()).toBe(TEST_ATTACHMENTS_RECORD_ID);
    expect(widget.recordId()).not.toBe(TEST_SERVICE_REQUEST_ID);
  });

  it('hides the attachments widget until the record is loaded', async () => {
    records.detail.mockReturnValue(NEVER);
    await render(ServiceRequestsStaffPermissionKeys.Queue);
    expect(fixture.debugElement.query(By.directive(RecordAttachmentsStubComponent))).toBeNull();
  });

  it('shows page feedback instead of the widget when the record carries no attachments id', async () => {
    records.detail.mockReturnValue(of(staffRecord({ attachmentsRecordId: '' })));
    await render(ServiceRequestsStaffPermissionKeys.Queue);
    expect(fixture.debugElement.query(By.directive(RecordAttachmentsStubComponent))).toBeNull();
    const feedback = allByTestId(host, 'detail-attachments-failed')[0]?.querySelector('.dga-page-feedback');
    expect(feedback?.getAttribute('data-kind')).toBe(DgaPageFeedbackKind.Error);
    expect(feedback?.textContent).toContain('errors.INTERNAL_ERROR');
  });

  it('renders the decisions from the allowed actions only', async () => {
    await render(ServiceRequestsStaffPermissionKeys.Queue, ServiceRequestsStaffPermissionKeys.Work);
    expect(decisionCodes().sort()).toEqual([ServiceRequestActions.Approve, ServiceRequestActions.Reject, ServiceRequestActions.Return].sort());
  });

  it('claims an unassigned request, after which the decisions the server allows appear', async () => {
    records.detail.mockReturnValue(of(staffRecord({ status: ServiceRequestStatus.Submitted, assignee: null, allowedActions: [] })));
    await render(ServiceRequestsStaffPermissionKeys.Queue, ServiceRequestsStaffPermissionKeys.Work);
    expect(decisionButtons()).toHaveLength(0);
    expect(host.textContent).toContain('patterns.staff-detail.no-actions');
    expect(allByTestId(host, 'detail-release')).toHaveLength(0);

    records.detail.mockReturnValue(of(recordIn(ServiceRequestStatus.Submitted, 'submitted', {
      allowedActions: [{ actionKey: ServiceRequestActions.StartReview, labelKey: 'serviceRequests.action.start-review', reason: 1, reasonFieldKey: null, toStatus: 3, toTerminal: false }],
    })));
    press(allByTestId(host, 'detail-claim')[0]);
    await settle(fixture);

    expect(queues.claim).toHaveBeenCalledWith(SERVICE_REQUEST_RECORD_TYPE, TEST_SERVICE_REQUEST_ID);
    expect(decisionCodes()).toEqual([ServiceRequestActions.StartReview]);
    expect(allByTestId(host, 'detail-claim')).toHaveLength(0);
    expect(allByTestId(host, 'detail-release')).toHaveLength(1);
  });

  it('hides claim from a queue reader without assignments.work', async () => {
    records.detail.mockReturnValue(of(staffRecord({ assignee: null })));
    await render(ServiceRequestsStaffPermissionKeys.Queue);
    expect(allByTestId(host, 'detail-claim')).toHaveLength(0);
  });

  it('opens the reason dialog for return and blocks an empty reason', async () => {
    await render(ServiceRequestsStaffPermissionKeys.Queue, ServiceRequestsStaffPermissionKeys.Work);
    await pressDecision(ServiceRequestActions.Return);
    expect(dialogOpen()).toBe(true);
    expect(records.transition).not.toHaveBeenCalled();

    await submitDialog();
    expect(records.transition).not.toHaveBeenCalled();
    expect(host.querySelector('.decision-dialog__error')?.textContent).toContain('patterns.decision-dialog.reason-required');

    await typeReason('   ');
    await submitDialog();
    expect(records.transition).not.toHaveBeenCalled();
    expect(dialogOpen()).toBe(true);
  });

  it('returns with the typed reason, closes the dialog and shows the new status', async () => {
    records.transition.mockReturnValue(of(recordIn(ServiceRequestStatus.Returned, 'returned', { allowedActions: [], lastReason: 'Attach the signed form.' })));
    await render(ServiceRequestsStaffPermissionKeys.Queue, ServiceRequestsStaffPermissionKeys.Work);
    await pressDecision(ServiceRequestActions.Return);
    await typeReason('  Attach the signed form.  ');
    await submitDialog();
    expect(records.transition).toHaveBeenCalledWith(SERVICE_REQUEST_RECORD_TYPE, TEST_SERVICE_REQUEST_ID, {
      action: ServiceRequestActions.Return,
      reason: 'Attach the signed form.',
    });
    expect(dialogOpen()).toBe(false);
    expect(allByTestId(host, 'detail-status')[0].textContent).toContain('serviceRequests.status.returned.staff');
    expect(host.textContent).toContain('Attach the signed form.');
  });

  it('approves at once, and the terminal request offers no further action', async () => {
    records.transition.mockReturnValue(of(recordIn(ServiceRequestStatus.Approved, 'approved')));
    await render(ServiceRequestsStaffPermissionKeys.Queue, ServiceRequestsStaffPermissionKeys.Work);
    await pressDecision(ServiceRequestActions.Approve);
    expect(records.transition).toHaveBeenCalledWith(SERVICE_REQUEST_RECORD_TYPE, TEST_SERVICE_REQUEST_ID, { action: ServiceRequestActions.Approve, reason: null });
    expect(dialogOpen()).toBe(false);
    expect(decisionButtons()).toHaveLength(0);
    expect(host.textContent).toContain('patterns.staff-detail.no-actions');
    expect(allByTestId(host, 'detail-decided')[0].textContent).toContain(Keys.Detail.DecisionDone);
    expect(allByTestId(host, 'detail-release')).toHaveLength(0);
  });

  it('rejects with a reason, and the rejected request has no action left', async () => {
    records.transition.mockReturnValue(of(recordIn(ServiceRequestStatus.Rejected, 'rejected')));
    await render(ServiceRequestsStaffPermissionKeys.Queue, ServiceRequestsStaffPermissionKeys.Work);
    await pressDecision(ServiceRequestActions.Reject);
    await typeReason('Outside the service scope.');
    await submitDialog();
    expect(records.transition).toHaveBeenCalledWith(SERVICE_REQUEST_RECORD_TYPE, TEST_SERVICE_REQUEST_ID, {
      action: ServiceRequestActions.Reject,
      reason: 'Outside the service scope.',
    });
    expect(decisionButtons()).toHaveLength(0);
  });

  it('shows "not found" for an id that is not a PublicId', async () => {
    grant(ServiceRequestsStaffPermissionKeys.Queue);
    fixture = TestBed.createComponent(ServiceRequestStaffDetailPageComponent);
    fixture.componentRef.setInput('id', 'bad');
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await settle(fixture);
    expect(records.detail).not.toHaveBeenCalled();
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('errors.RECORDS_NOT_FOUND');
  });
});
