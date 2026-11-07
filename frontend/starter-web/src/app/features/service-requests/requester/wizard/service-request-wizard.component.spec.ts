import { Location } from '@angular/common';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { Router } from '@angular/router';
import { DgaPageFeedbackKind } from '@codelab/ng-dga';
import { HttpStatus } from '@codelab/ng-security';
import { NEVER, of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { RecordAttachmentsComponent } from '../../../../shared/record-attachments/record-attachments.component';
import {
  SERVICE_REQUEST_ATTACHABLE_RECORD_TYPE,
  SERVICE_REQUEST_RECORD_TYPE,
  ServiceRequestDraftStep,
  ServiceRequestStatus,
  ServiceRequestValidationCodes,
} from '../../data';
import { ServiceRequestsRequesterTranslationKeys as Keys } from '../requester-translation-keys';
import {
  StubRecordAttachmentsComponent,
  TEST_ATTACHMENTS_RECORD_ID,
  TEST_CATEGORY_ID,
  TEST_PHONE,
  TEST_SERVICE_REQUEST_ID,
  aDraft,
  aRecord,
  clientProviders,
  failing,
  fakeRecordsClient,
  fakeServiceRequestsClient,
  httpRefusal,
  requesterTestProviders,
} from '../requester.testing';
import { ServiceRequestWizardComponent } from './service-request-wizard.component';

const DESCRIPTION = 'A description that is long enough.';

interface Rendered {
  readonly fixture: ComponentFixture<ServiceRequestWizardComponent>;
  readonly page: ServiceRequestWizardComponent;
  readonly root: HTMLElement;
}

interface RenderOptions {
  readonly id?: string;
  readonly requests?: ReturnType<typeof fakeServiceRequestsClient>;
  readonly records?: ReturnType<typeof fakeRecordsClient>;
}

async function render(options: RenderOptions = {}): Promise<Rendered> {
  TestBed.configureTestingModule({
    imports: [ServiceRequestWizardComponent],
    providers: [
      ...requesterTestProviders(),
      ...clientProviders({ requests: options.requests, records: options.records }),
      { provide: Location, useValue: { replaceState: vi.fn() } },
    ],
  });
  TestBed.overrideComponent(ServiceRequestWizardComponent, {
    remove: { imports: [RecordAttachmentsComponent] },
    add: { imports: [StubRecordAttachmentsComponent] },
  });
  const fixture = TestBed.createComponent(ServiceRequestWizardComponent);
  if (options.id !== undefined) fixture.componentRef.setInput('id', options.id);
  await settle(fixture);
  return { fixture, page: fixture.componentInstance, root: fixture.nativeElement as HTMLElement };
}

async function settle(fixture: ComponentFixture<ServiceRequestWizardComponent>): Promise<void> {
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
}

async function press(rendered: Rendered, selector: string): Promise<void> {
  const button = rendered.root.querySelector<HTMLButtonElement>(`${selector} button`);
  expect(button, selector).not.toBeNull();
  button?.click();
  await settle(rendered.fixture);
  await vi.waitFor(() => expect(rendered.page['store'].busy()).toBe(false));
  await settle(rendered.fixture);
}

function currentStep(rendered: Rendered): string | null {
  return rendered.root.querySelector('.wizard__body')?.getAttribute('data-step') ?? null;
}

function attachmentsWidget(rendered: Rendered): StubRecordAttachmentsComponent | null {
  const element = rendered.fixture.debugElement.query((candidate) => candidate.componentInstance instanceof StubRecordAttachmentsComponent);
  return element ? (element.componentInstance as StubRecordAttachmentsComponent) : null;
}

/** A draft whose steps 1 and 2 are complete, so a resume opens on the attachments step. */
function completeDraft() {
  return aDraft({ categoryId: TEST_CATEGORY_ID, title: 'A valid title', description: DESCRIPTION, contactPhone: TEST_PHONE });
}

function fillStep1(rendered: Rendered): void {
  rendered.page.setValue('categoryId', TEST_CATEGORY_ID);
  rendered.page.setValue('title', 'A valid title');
}

function fillStep2(rendered: Rendered): void {
  rendered.page.setValue('description', DESCRIPTION);
  rendered.page.setValue('contactPhone', TEST_PHONE);
}

describe('ServiceRequestWizardComponent', () => {
  it('starts a new request on step 1 with the categories and no draft', async () => {
    const requests = fakeServiceRequestsClient();
    const rendered = await render({ requests });
    expect(currentStep(rendered)).toBe('request');
    expect(rendered.root.querySelector('h1')?.textContent).toContain(Keys.Wizard.TitleNew);
    expect(rendered.root.querySelector('[data-testid="sr-field-categoryId"] option:last-child')?.textContent).toContain('Certificates');
    expect(requests.create).not.toHaveBeenCalled();
  });

  it('does not leave an incomplete step: Next is refused, nothing is saved, every message of the step shows', async () => {
    const requests = fakeServiceRequestsClient();
    const rendered = await render({ requests });
    await press(rendered, '.wizard__next');
    expect(currentStep(rendered)).toBe('request');
    expect(requests.saveDraft).not.toHaveBeenCalled();
    expect(rendered.root.textContent).toContain(`serviceRequests.errors.${ServiceRequestValidationCodes.TitleRequired}`);
    expect(rendered.root.textContent).toContain(`serviceRequests.errors.${ServiceRequestValidationCodes.CategoryRequired}`);
  });

  it('moves on from a complete step and saves it (creating the draft once)', async () => {
    const requests = fakeServiceRequestsClient();
    const rendered = await render({ requests });
    fillStep1(rendered);
    await press(rendered, '.wizard__next');
    expect(currentStep(rendered)).toBe('details');
    expect(requests.create).toHaveBeenCalledTimes(1);
    expect(requests.saveDraft).toHaveBeenCalledWith(TEST_SERVICE_REQUEST_ID, expect.objectContaining({ step: ServiceRequestDraftStep.Request }));

    fillStep2(rendered);
    await press(rendered, '.wizard__next');
    expect(currentStep(rendered)).toBe('attachments');
    expect(requests.create).toHaveBeenCalledTimes(1);
    expect(attachmentsWidget(rendered)?.recordId()).toBe(TEST_ATTACHMENTS_RECORD_ID);
    expect(attachmentsWidget(rendered)?.recordId()).not.toBe(TEST_SERVICE_REQUEST_ID);
    expect(attachmentsWidget(rendered)?.recordType()).toBe(SERVICE_REQUEST_ATTACHABLE_RECORD_TYPE);
  });

  it('reads the attachments id of a new draft from its record once the draft is created', async () => {
    const records = fakeRecordsClient();
    const rendered = await render({ records });
    expect(records.detail).not.toHaveBeenCalled();
    fillStep1(rendered);
    await press(rendered, '.wizard__next');
    expect(records.detail).toHaveBeenCalledTimes(1);
    expect(records.detail).toHaveBeenCalledWith(SERVICE_REQUEST_RECORD_TYPE, TEST_SERVICE_REQUEST_ID);
  });

  it('gives a resumed draft the attachments id of its record, never the request id', async () => {
    const records = fakeRecordsClient({ detail: vi.fn(() => of(aRecord(ServiceRequestStatus.Draft))) });
    const requests = fakeServiceRequestsClient({ getDraft: vi.fn(() => of(completeDraft())) });
    const rendered = await render({ id: TEST_SERVICE_REQUEST_ID, requests, records });
    expect(currentStep(rendered)).toBe('attachments');
    expect(records.detail).toHaveBeenCalledWith(SERVICE_REQUEST_RECORD_TYPE, TEST_SERVICE_REQUEST_ID);
    expect(attachmentsWidget(rendered)?.recordId()).toBe(TEST_ATTACHMENTS_RECORD_ID);
    expect(attachmentsWidget(rendered)?.recordId()).not.toBe(TEST_SERVICE_REQUEST_ID);
  });

  it('hides the attachments widget until the attachments id is known', async () => {
    const records = fakeRecordsClient({ detail: vi.fn(() => NEVER) });
    const requests = fakeServiceRequestsClient({ getDraft: vi.fn(() => of(completeDraft())) });
    const rendered = await render({ id: TEST_SERVICE_REQUEST_ID, requests, records });
    expect(currentStep(rendered)).toBe('attachments');
    expect(attachmentsWidget(rendered)).toBeNull();
    expect(rendered.root.querySelector('[data-testid="sr-wizard-attachments-failed"]')).toBeNull();
  });

  it('shows page feedback when the attachments id cannot be read, and reads it again on retry', async () => {
    const detail = vi
      .fn()
      .mockReturnValueOnce(failing(httpRefusal(HttpStatus.serviceUnavailable, 'RETRYABLE')))
      .mockReturnValue(of(aRecord(ServiceRequestStatus.Draft)));
    const requests = fakeServiceRequestsClient({ getDraft: vi.fn(() => of(completeDraft())) });
    const rendered = await render({ id: TEST_SERVICE_REQUEST_ID, requests, records: fakeRecordsClient({ detail }) });
    expect(attachmentsWidget(rendered)).toBeNull();
    const feedback = rendered.root.querySelector('[data-testid="sr-wizard-attachments-failed"] .dga-page-feedback');
    expect(feedback?.getAttribute('data-kind')).toBe(DgaPageFeedbackKind.Error);
    expect(feedback?.textContent).toContain('errors.RETRYABLE');

    await press(rendered, '[data-testid="sr-wizard-attachments-retry"]');
    expect(detail).toHaveBeenCalledTimes(2);
    expect(rendered.root.querySelector('[data-testid="sr-wizard-attachments-failed"]')).toBeNull();
    expect(attachmentsWidget(rendered)?.recordId()).toBe(TEST_ATTACHMENTS_RECORD_ID);
  });

  it('saves the draft on demand and says so', async () => {
    const requests = fakeServiceRequestsClient();
    const rendered = await render({ requests });
    rendered.page.setValue('title', 'Half done');
    await press(rendered, '.wizard__draft');
    expect(requests.saveDraft).toHaveBeenCalledWith(
      TEST_SERVICE_REQUEST_ID,
      expect.objectContaining({ step: ServiceRequestDraftStep.Request, fields: expect.objectContaining({ title: 'Half done' }) }),
    );
    expect(rendered.root.textContent).toContain(Keys.Wizard.DraftSaved);
  });

  it('resumes a saved draft on the first step that still needs input', async () => {
    const draft = aDraft({ categoryId: TEST_CATEGORY_ID, title: 'A valid title', description: DESCRIPTION, contactPhone: TEST_PHONE });
    const requests = fakeServiceRequestsClient({ getDraft: vi.fn(() => of(draft)) });
    const rendered = await render({ id: TEST_SERVICE_REQUEST_ID, requests });
    expect(requests.getDraft).toHaveBeenCalledWith(TEST_SERVICE_REQUEST_ID);
    expect(currentStep(rendered)).toBe('attachments');
    expect(rendered.root.querySelector('h1')?.textContent).toContain(Keys.Wizard.TitleResume);
    expect(rendered.root.querySelector('[data-field="title"]')?.textContent).toContain('A valid title');
    expect(rendered.root.querySelector('[data-field="categoryId"]')?.textContent).toContain('Certificates');
    expect(requests.create).not.toHaveBeenCalled();
  });

  it('shows a draft it cannot load with Retry', async () => {
    const getDraft = vi.fn().mockReturnValueOnce(failing(httpRefusal(HttpStatus.notFound, 'RECORDS_NOT_FOUND'))).mockReturnValue(of(aDraft()));
    const rendered = await render({ id: TEST_SERVICE_REQUEST_ID, requests: fakeServiceRequestsClient({ getDraft }) });
    expect(rendered.root.querySelector('[data-testid="sr-wizard-load-failed"]')?.textContent).toContain('errors.RECORDS_NOT_FOUND');
    await press(rendered, '[data-testid="sr-wizard-retry"]');
    expect(rendered.root.querySelector('[data-testid="sr-wizard-load-failed"]')).toBeNull();
    expect(currentStep(rendered)).toBe('request');
  });

  it('refuses a malformed resume id without calling the API', async () => {
    const requests = fakeServiceRequestsClient();
    const rendered = await render({ id: 'not-an-id', requests });
    expect(requests.getDraft).not.toHaveBeenCalled();
    expect(rendered.root.querySelector('[data-testid="sr-wizard-load-failed"]')).not.toBeNull();
  });

  it('submits and shows the allocated number', async () => {
    const draft = aDraft({ categoryId: TEST_CATEGORY_ID, title: 'A valid title', description: DESCRIPTION, contactPhone: TEST_PHONE });
    const requests = fakeServiceRequestsClient({ getDraft: vi.fn(() => of(draft)) });
    const rendered = await render({ id: TEST_SERVICE_REQUEST_ID, requests });
    await press(rendered, '.wizard__submit');
    expect(requests.submit).toHaveBeenCalledWith(TEST_SERVICE_REQUEST_ID);
    expect(rendered.root.querySelector('[data-testid="sr-wizard-number"]')?.textContent).toBe('SR-2026-001');
    expect(rendered.root.querySelector('[data-testid="sr-wizard-view"]')?.getAttribute('href')).toBe(`/service-requests/${TEST_SERVICE_REQUEST_ID}`);
  });

  it('maps a refused submit to the step and field that own the error', async () => {
    const draft = aDraft({ categoryId: TEST_CATEGORY_ID, title: 'A valid title', description: DESCRIPTION, contactPhone: TEST_PHONE });
    const refusal = httpRefusal(HttpStatus.badRequest, 'VALIDATION_FAILED', { title: [ServiceRequestValidationCodes.TitleTooShort] });
    const requests = fakeServiceRequestsClient({ getDraft: vi.fn(() => of(draft)), submit: vi.fn(() => failing(refusal)) });
    const rendered = await render({ id: TEST_SERVICE_REQUEST_ID, requests });
    await press(rendered, '.wizard__submit');
    expect(currentStep(rendered)).toBe('request');
    const titleField = rendered.root.querySelector('[data-testid="sr-field-title"]');
    expect(titleField?.textContent).toContain(`serviceRequests.errors.${ServiceRequestValidationCodes.TitleTooShort}`);
    expect(rendered.root.querySelector('.wizard__errors')?.textContent).toContain('errors.VALIDATION_FAILED');
    expect(rendered.root.querySelector('.wizard__errors')?.textContent).not.toContain('errors.SERVICE_REQUESTS_');
  });

  it('keeps the requester on the attachments step when the required document is missing', async () => {
    const draft = aDraft({ categoryId: TEST_CATEGORY_ID, title: 'A valid title', description: DESCRIPTION, contactPhone: TEST_PHONE });
    const requests = fakeServiceRequestsClient({
      getDraft: vi.fn(() => of(draft)),
      submit: vi.fn(() => failing(httpRefusal(HttpStatus.conflict, 'ATTACHMENTS_REQUIRED_MISSING'))),
    });
    const rendered = await render({ id: TEST_SERVICE_REQUEST_ID, requests });
    await press(rendered, '.wizard__submit');
    expect(currentStep(rendered)).toBe('attachments');
    expect(rendered.root.querySelector('[data-testid="sr-wizard-attachments-missing"]')).not.toBeNull();
    expect(rendered.root.querySelector('.wizard__errors')?.textContent).toContain('errors.ATTACHMENTS_REQUIRED_MISSING');
    expect(rendered.root.querySelector('[data-testid="sr-wizard-number"]')).toBeNull();
  });

  it('works without any DevAutofill data set registered (renders no autofill buttons)', async () => {
    const rendered = await render();
    expect(rendered.root.querySelector('dga-dev-autofill')).not.toBeNull();
    expect(rendered.root.querySelector('[data-testid="dev-autofill"]')).toBeNull();
  });

  it('cancel leaves for my requests', async () => {
    const rendered = await render();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    await press(rendered, '.wizard__cancel');
    expect(navigate).toHaveBeenCalledWith('/service-requests/mine');
  });
});
