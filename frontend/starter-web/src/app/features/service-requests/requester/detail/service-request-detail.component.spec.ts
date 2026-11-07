import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { DgaConfirmDialogComponent, DgaPageFeedbackKind } from '@codelab/ng-dga';
import { ApiErrorCodes, HttpStatus } from '@codelab/ng-security';
import { NEVER, of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { RecordAttachmentsComponent } from '../../../../shared/record-attachments/record-attachments.component';
import { SERVICE_REQUEST_ATTACHABLE_RECORD_TYPE, ServiceRequestActions, ServiceRequestStatus } from '../../data';
import { ServiceRequestsRequesterTranslationKeys as Keys } from '../requester-translation-keys';
import {
  StubRecordAttachmentsComponent,
  TEST_ATTACHMENTS_RECORD_ID,
  TEST_SERVICE_REQUEST_ID,
  aRecord,
  aStep,
  cancelAction,
  clientProviders,
  failing,
  fakeRecordsClient,
  fakeServiceRequestsClient,
  httpRefusal,
  requesterTestProviders,
  resubmitAction,
} from '../requester.testing';
import { ServiceRequestDetailComponent } from './service-request-detail.component';

interface Rendered {
  readonly fixture: ComponentFixture<ServiceRequestDetailComponent>;
  readonly root: HTMLElement;
}

async function render(records = fakeRecordsClient(), id: string = TEST_SERVICE_REQUEST_ID, requests = fakeServiceRequestsClient()): Promise<Rendered> {
  TestBed.configureTestingModule({
    imports: [ServiceRequestDetailComponent],
    providers: [...requesterTestProviders(), ...clientProviders({ records, requests })],
  });
  TestBed.overrideComponent(ServiceRequestDetailComponent, {
    remove: { imports: [RecordAttachmentsComponent] },
    add: { imports: [StubRecordAttachmentsComponent] },
  });
  const fixture = TestBed.createComponent(ServiceRequestDetailComponent);
  fixture.componentRef.setInput('id', id);
  await settle(fixture);
  return { fixture, root: fixture.nativeElement as HTMLElement };
}

async function settle(fixture: ComponentFixture<ServiceRequestDetailComponent>): Promise<void> {
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
}

function actionKeys(root: HTMLElement): (string | undefined)[] {
  return Array.from(root.querySelectorAll<HTMLElement>('[data-testid="sr-detail-actions"] dga-button[data-action]')).map(
    (button) => button.dataset['action'],
  );
}

function withRecord(status: ServiceRequestStatus, overrides: Parameters<typeof aRecord>[1] = {}) {
  return fakeRecordsClient({ detail: vi.fn(() => of(aRecord(status, overrides))) });
}

function attachments(rendered: Rendered): StubRecordAttachmentsComponent | null {
  const element = rendered.fixture.debugElement.query(By.directive(StubRecordAttachmentsComponent));
  return element ? (element.componentInstance as StubRecordAttachmentsComponent) : null;
}

describe('ServiceRequestDetailComponent', () => {
  it('shows the number, the requester wording, the fields, the timeline and the tracker', async () => {
    const { root } = await render();
    expect(root.querySelector('.requester-detail__record-number')?.textContent).toBe('SR-2026-001');
    expect(root.querySelector('.requester-detail__status')?.textContent).toContain('serviceRequests.status.submitted.requester');
    expect(root.querySelector('app-record-detail-cards')?.textContent).toContain('Experience certificate');
    expect(root.querySelector('app-record-timeline')?.textContent).toContain('serviceRequests.action.submit');
    expect(root.querySelector('app-status-tracker')?.textContent).toContain('serviceRequests.status.submitted.requester');
  });

  it('offers exactly the allowed actions of the server: cancel on a submitted request', async () => {
    const { root } = await render();
    expect(actionKeys(root)).toEqual([ServiceRequestActions.Cancel]);
  });

  it('offers resubmit on a returned request and shows the return reason', async () => {
    const rendered = await render(withRecord(ServiceRequestStatus.Returned, { allowedActions: [resubmitAction()], lastReason: 'Add the signed page.' }));
    expect(actionKeys(rendered.root)).toEqual([ServiceRequestActions.Resubmit]);
    expect(rendered.root.querySelector('[data-testid="sr-detail-reason"]')?.textContent).toBe('Add the signed page.');
    expect(rendered.root.querySelector('[data-testid="sr-detail-banner"]')?.textContent).toContain(Keys.Detail.ReturnedTitle);
    expect(attachments(rendered)?.readonly()).toBe(false);
  });

  it('offers nothing after a rejection (no resubmit) and shows the reason', async () => {
    const rendered = await render(withRecord(ServiceRequestStatus.Rejected, { lastReason: 'Out of scope.' }));
    expect(rendered.root.querySelector('[data-testid="sr-detail-actions"]')).toBeNull();
    expect(rendered.root.querySelector('app-decision-panel')).toBeNull();
    expect(rendered.root.querySelector('[data-testid="sr-detail-banner"]')?.textContent).toContain(Keys.Detail.RejectedTitle);
    expect(attachments(rendered)?.readonly()).toBe(true);
  });

  it('never adds an action the server did not send, even when the status would allow it elsewhere', async () => {
    const rendered = await render(withRecord(ServiceRequestStatus.Returned, { allowedActions: [] }));
    expect(rendered.root.querySelector('app-decision-panel')).toBeNull();
  });

  it('hides a stale action list on a terminal record', async () => {
    const rendered = await render(withRecord(ServiceRequestStatus.Approved, { allowedActions: [cancelAction()] }));
    expect(rendered.root.querySelector('app-decision-panel')).toBeNull();
  });

  it('lists the attachments read-only on a submitted request, under the attachments id (never the request id)', async () => {
    const rendered = await render();
    expect(attachments(rendered)?.recordId()).toBe(TEST_ATTACHMENTS_RECORD_ID);
    expect(attachments(rendered)?.recordId()).not.toBe(TEST_SERVICE_REQUEST_ID);
    expect(attachments(rendered)?.recordType()).toBe(SERVICE_REQUEST_ATTACHABLE_RECORD_TYPE);
    expect(attachments(rendered)?.readonly()).toBe(true);
  });

  it('shows no attachments while the record loads', async () => {
    const rendered = await render(fakeRecordsClient({ detail: vi.fn(() => NEVER) }));
    expect(attachments(rendered)).toBeNull();
    expect(rendered.root.querySelector('[data-testid="sr-detail-attachments-failed"]')).toBeNull();
  });

  it('shows page feedback instead of the widget when the record carries no attachments id', async () => {
    const rendered = await render(withRecord(ServiceRequestStatus.Submitted, { attachmentsRecordId: '' }));
    expect(attachments(rendered)).toBeNull();
    const feedback = rendered.root.querySelector('[data-testid="sr-detail-attachments-failed"] .dga-page-feedback');
    expect(feedback?.getAttribute('data-kind')).toBe(DgaPageFeedbackKind.Error);
    expect(feedback?.textContent).toContain(`errors.${ApiErrorCodes.internalError}`);
  });

  it('shows no fields and no attachments for a draft (it is edited in the wizard)', async () => {
    const requests = fakeServiceRequestsClient();
    const rendered = await render(withRecord(ServiceRequestStatus.Draft), TEST_SERVICE_REQUEST_ID, requests);
    expect(requests.get).not.toHaveBeenCalled();
    expect(attachments(rendered)).toBeNull();
  });

  it('cancels with the optional reason through the transitions endpoint and shows the new status', async () => {
    const records = fakeRecordsClient({
      transition: vi.fn(() => of(aRecord(ServiceRequestStatus.Cancelled))),
    });
    const rendered = await render(records);
    records.timeline.mockReturnValue(of([aStep(), aStep({ actionKey: ServiceRequestActions.Cancel, toStatusKey: 'cancelled' })]));
    rendered.root.querySelector<HTMLButtonElement>(`dga-button[data-action="${ServiceRequestActions.Cancel}"] button`)?.click();
    await settle(rendered.fixture);
    const dialog = rendered.fixture.debugElement.query(By.directive(DgaConfirmDialogComponent)).componentInstance as DgaConfirmDialogComponent;
    dialog.confirm();
    await settle(rendered.fixture);
    expect(records.transition).toHaveBeenCalledWith('service-request', TEST_SERVICE_REQUEST_ID, { action: ServiceRequestActions.Cancel, reason: null });
    expect(rendered.root.querySelector('.requester-detail__status')?.textContent).toContain('serviceRequests.status.cancelled.requester');
    expect(rendered.root.querySelector('app-decision-panel')).toBeNull();
  });

  it('resubmits a returned request at once (no reason asked)', async () => {
    const records = fakeRecordsClient({
      detail: vi.fn(() => of(aRecord(ServiceRequestStatus.Returned, { allowedActions: [resubmitAction()] }))),
      transition: vi.fn(() => of(aRecord(ServiceRequestStatus.Resubmitted, { allowedActions: [] }))),
    });
    const rendered = await render(records);
    rendered.root.querySelector<HTMLButtonElement>(`dga-button[data-action="${ServiceRequestActions.Resubmit}"] button`)?.click();
    await settle(rendered.fixture);
    expect(records.transition).toHaveBeenCalledWith('service-request', TEST_SERVICE_REQUEST_ID, { action: ServiceRequestActions.Resubmit, reason: null });
    expect(rendered.root.querySelector('.requester-detail__status')?.textContent).toContain('serviceRequests.status.resubmitted.requester');
  });

  it('shows a failed load with its message and Retry', async () => {
    const detail = vi.fn().mockReturnValueOnce(failing(httpRefusal(HttpStatus.notFound, 'RECORDS_NOT_FOUND'))).mockReturnValue(of(aRecord()));
    const rendered = await render(fakeRecordsClient({ detail }));
    expect(rendered.root.querySelector('.requester-detail__error')?.textContent).toContain('errors.RECORDS_NOT_FOUND');
    rendered.root.querySelector<HTMLButtonElement>('.requester-detail__retry button')?.click();
    await settle(rendered.fixture);
    expect(rendered.root.querySelector('.requester-detail__error')).toBeNull();
    expect(detail).toHaveBeenCalledTimes(2);
  });

  it('refuses a malformed id without calling the API', async () => {
    const records = fakeRecordsClient();
    const rendered = await render(records, 'not-an-id');
    expect(records.detail).not.toHaveBeenCalled();
    expect(rendered.root.querySelector('.requester-detail__error')?.textContent).toContain('errors.RECORDS_NOT_FOUND');
  });
});
