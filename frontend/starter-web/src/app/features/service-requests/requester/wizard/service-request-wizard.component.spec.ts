import { Location } from '@angular/common';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { Router } from '@angular/router';
import { HttpStatus } from '@codelab/ng-security';
import { of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { RecordAttachmentsComponent } from '../../../../shared/record-attachments/record-attachments.component';
import { ServiceRequestDraftStep, ServiceRequestValidationCodes } from '../../data';
import { ServiceRequestsRequesterTranslationKeys as Keys } from '../requester-translation-keys';
import {
  StubRecordAttachmentsComponent,
  TEST_CATEGORY_ID,
  TEST_PHONE,
  TEST_SERVICE_REQUEST_ID,
  aDraft,
  clientProviders,
  failing,
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

async function render(options: { readonly id?: string; readonly requests?: ReturnType<typeof fakeServiceRequestsClient> } = {}): Promise<Rendered> {
  TestBed.configureTestingModule({
    imports: [ServiceRequestWizardComponent],
    providers: [
      ...requesterTestProviders(),
      ...clientProviders({ requests: options.requests }),
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
    const attachments = rendered.fixture.debugElement.query((element) => element.componentInstance instanceof StubRecordAttachmentsComponent);
    expect(attachments.componentInstance.recordId()).toBe(TEST_SERVICE_REQUEST_ID);
    expect(attachments.componentInstance.recordType()).toBe('service-request');
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
