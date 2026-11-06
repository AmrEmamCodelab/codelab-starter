import { Location } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { HttpStatus } from '@codelab/ng-security';
import { Subject, of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { ServiceRequestDraftStep, ServiceRequestValidationCodes } from '../../data';
import {
  TEST_CATEGORY_ID,
  TEST_PHONE,
  TEST_SERVICE_REQUEST_ID,
  aDraft,
  clientProviders,
  failing,
  fakeCategoriesClient,
  fakeServiceRequestsClient,
  httpRefusal,
  requesterTestProviders,
} from '../requester.testing';
import { ServiceRequestWizardStore, WizardLoadState } from './service-request-wizard.store';

function setUp(requests = fakeServiceRequestsClient(), categories = fakeCategoriesClient()) {
  const location = { replaceState: vi.fn() };
  TestBed.configureTestingModule({
    providers: [
      ...requesterTestProviders(),
      ...clientProviders({ requests, categories }),
      { provide: Location, useValue: location },
      ServiceRequestWizardStore,
    ],
  });
  return { store: TestBed.inject(ServiceRequestWizardStore), requests, categories, location };
}

function fill(store: ServiceRequestWizardStore): void {
  store.form.setValue({
    categoryId: TEST_CATEGORY_ID,
    title: 'A valid title',
    description: 'A description that is long enough.',
    contactPhone: TEST_PHONE,
    preferredDate: null,
  });
}

describe('ServiceRequestWizardStore', () => {
  it('starts a new request with the categories and no draft yet', () => {
    const { store, requests, categories } = setUp();
    store.start(null);
    expect(categories.list).toHaveBeenCalledTimes(1);
    expect(requests.getDraft).not.toHaveBeenCalled();
    expect(store.load()).toBe(WizardLoadState.Ready);
    expect(store.id()).toBeNull();
    expect(store.resuming()).toBe(false);
  });

  it('creates the draft once on the first save, then only saves, and moves the URL to the resume link', async () => {
    const { store, requests, location } = setUp();
    store.start(null);
    fill(store);
    const first = store.saveStep(ServiceRequestDraftStep.Request);
    const second = store.saveStep(ServiceRequestDraftStep.Details);
    expect(await first).toBe(true);
    expect(await second).toBe(true);
    expect(requests.create).toHaveBeenCalledTimes(1);
    expect(requests.saveDraft).toHaveBeenNthCalledWith(1, TEST_SERVICE_REQUEST_ID, {
      fields: expect.objectContaining({ title: 'A valid title', categoryId: TEST_CATEGORY_ID }),
      step: ServiceRequestDraftStep.Request,
    });
    expect(requests.saveDraft).toHaveBeenLastCalledWith(TEST_SERVICE_REQUEST_ID, expect.objectContaining({ step: ServiceRequestDraftStep.Details }));
    expect(location.replaceState).toHaveBeenCalledWith(`/service-requests/drafts/${TEST_SERVICE_REQUEST_ID}`);
    expect(store.id()).toBe(TEST_SERVICE_REQUEST_ID);
  });

  it('is busy while a save runs', async () => {
    const answer = new Subject<void>();
    const { store } = setUp(fakeServiceRequestsClient({ saveDraft: vi.fn(() => answer) }));
    store.start(null);
    const saving = store.saveDraft(ServiceRequestDraftStep.Request);
    await vi.waitFor(() => expect(store.busy()).toBe(true));
    answer.next();
    answer.complete();
    expect(await saving).toBe(true);
    expect(store.busy()).toBe(false);
    expect(store.draftSaved()).toBe(true);
  });

  it('resumes a draft: fills the form and opens the first step that needs input', () => {
    const draft = aDraft({ categoryId: TEST_CATEGORY_ID, title: 'A valid title' });
    const { store, requests } = setUp(fakeServiceRequestsClient({ getDraft: vi.fn(() => of(draft)) }));
    store.start(TEST_SERVICE_REQUEST_ID);
    expect(requests.getDraft).toHaveBeenCalledWith(TEST_SERVICE_REQUEST_ID);
    expect(store.load()).toBe(WizardLoadState.Ready);
    expect(store.form.controls.title.value).toBe('A valid title');
    expect(store.startStep()).toBe(1);
    expect(store.id()).toBe(TEST_SERVICE_REQUEST_ID);
    expect(store.resuming()).toBe(true);
  });

  it('reports a draft it cannot load, and loads it again on retry', () => {
    const getDraft = vi.fn().mockReturnValueOnce(failing(httpRefusal(HttpStatus.notFound, 'RECORDS_NOT_FOUND'))).mockReturnValue(of(aDraft()));
    const { store } = setUp(fakeServiceRequestsClient({ getDraft }));
    store.start(TEST_SERVICE_REQUEST_ID);
    expect(store.load()).toBe(WizardLoadState.Failed);
    expect(store.loadError()?.code).toBe('RECORDS_NOT_FOUND');
    store.retry();
    expect(store.load()).toBe(WizardLoadState.Ready);
  });

  it('treats a malformed resume id as a request that does not exist, without a call', () => {
    const { store, requests } = setUp();
    store.startUnknown();
    expect(requests.getDraft).not.toHaveBeenCalled();
    expect(store.load()).toBe(WizardLoadState.Failed);
    expect(store.loadError()?.status).toBe(HttpStatus.notFound);
  });

  it('saves every field with the last step, then submits and keeps the number', async () => {
    const { store, requests } = setUp();
    store.start(null);
    fill(store);
    expect(await store.submit()).toBe(true);
    expect(requests.saveDraft).toHaveBeenCalledWith(TEST_SERVICE_REQUEST_ID, expect.objectContaining({ step: ServiceRequestDraftStep.Attachments }));
    expect(requests.submit).toHaveBeenCalledWith(TEST_SERVICE_REQUEST_ID);
    expect(store.submittedNumber()).toBe('SR-2026-001');
  });

  it('maps a 400 to the fields in the module wording', async () => {
    const refusal = httpRefusal(HttpStatus.badRequest, 'VALIDATION_FAILED', {
      title: [ServiceRequestValidationCodes.TitleTooShort],
      description: ['REQUIRED'],
    });
    const { store } = setUp(fakeServiceRequestsClient({ submit: vi.fn(() => failing(refusal)) }));
    store.start(null);
    expect(await store.submit()).toBe(false);
    expect(store.error()?.code).toBe('VALIDATION_FAILED');
    expect(store.fieldErrors()).toEqual({
      title: [`serviceRequests.errors.${ServiceRequestValidationCodes.TitleTooShort}`],
      description: ['errors.REQUIRED'],
    });
    expect(store.attachmentsMissing()).toBe(false);
    expect(store.submittedNumber()).toBeNull();
  });

  it('flags the attachment interlock', async () => {
    const { store } = setUp(fakeServiceRequestsClient({ submit: vi.fn(() => failing(httpRefusal(HttpStatus.conflict, 'ATTACHMENTS_REQUIRED_MISSING'))) }));
    store.start(null);
    expect(await store.submit()).toBe(false);
    expect(store.attachmentsMissing()).toBe(true);
  });

  it('does not submit when the last save is refused', async () => {
    const { store, requests } = setUp(fakeServiceRequestsClient({ saveDraft: vi.fn(() => failing(httpRefusal(HttpStatus.badRequest, 'VALIDATION_FAILED'))) }));
    store.start(null);
    expect(await store.submit()).toBe(false);
    expect(requests.submit).not.toHaveBeenCalled();
  });

  it('clears the previous refusal when the next call starts', async () => {
    const saveDraft = vi.fn().mockReturnValueOnce(failing(httpRefusal(HttpStatus.badRequest, 'VALIDATION_FAILED', { title: ['X'] }))).mockReturnValue(of(undefined));
    const { store } = setUp(fakeServiceRequestsClient({ saveDraft }));
    store.start(null);
    expect(await store.saveDraft(ServiceRequestDraftStep.Request)).toBe(false);
    expect(store.draftSaved()).toBe(false);
    expect(await store.saveDraft(ServiceRequestDraftStep.Request)).toBe(true);
    expect(store.error()).toBeNull();
    expect(store.fieldErrors()).toEqual({});
  });

  it('reports categories it cannot load and loads them again on retry', () => {
    const list = vi.fn().mockReturnValueOnce(failing(httpRefusal(HttpStatus.serverError, 'INTERNAL_ERROR'))).mockReturnValue(of([]));
    const { store } = setUp(undefined, { list });
    store.start(null);
    expect(store.categoriesError()?.code).toBe('INTERNAL_ERROR');
    store.retry();
    expect(store.categoriesError()).toBeNull();
    expect(list).toHaveBeenCalledTimes(2);
  });

  it('counts form changes so views over the form follow them', () => {
    const { store } = setUp();
    const before = store.revision();
    store.form.controls.title.setValue('x');
    expect(store.revision()).toBeGreaterThan(before);
  });
});
