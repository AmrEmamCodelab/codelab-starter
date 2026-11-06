import { Location } from '@angular/common';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpStatus, type ApiError } from '@codelab/ng-security';
import { firstValueFrom } from 'rxjs';
import { APP_CLOCK } from '../../../../core/session/clock';
import { NO_FIELD_ERRORS, type FieldErrorMap } from '../../../../patterns';
import { AttachmentErrorCodes } from '../../../../shared/attachments/attachment-error-codes';
import { RECORDS_TRANSLATED_CODES, RecordsErrorCodes, recordsApiErrorFrom } from '../../../../shared/records';
import {
  ServiceRequestDraftStep,
  ServiceRequestsClient,
  serviceRequestFieldErrorsFrom,
  todayInTimeZone,
  type ServiceRequestId,
} from '../../data';
import { SERVICE_REQUESTS_REQUESTER_BASE_LINK, serviceRequestsRequesterLinks } from '../requester-paths';
import { ServiceRequestCategoriesClient, type ServiceRequestCategory } from '../service-request-categories.client';
import { createServiceRequestWizardForm, draftFieldsOf, patchFromDraft, resumeStepIndex } from './service-request-wizard.form';

/** Where the wizard stands before the user can type: a new request is ready at once; a resumed draft loads first. */
export enum WizardLoadState {
  Ready = 1,
  Loading = 2,
  Failed = 3,
}

/** The codes the wizard has `errors.<CODE>` text for: the platform, record and attachment refusals. */
export const WIZARD_TRANSLATED_CODES: ReadonlySet<string> = new Set([
  ...RECORDS_TRANSLATED_CODES,
  AttachmentErrorCodes.RequiredMissing,
]);

const FIRST_STEP = 0;

/**
 * The state of the new-request wizard (provided by the page). It owns the form, the draft's id and every call:
 *
 * - The draft is created on the first save (Next or Save draft), then the page URL becomes the draft's resume link
 *   (`/service-requests/drafts/:id`) without a navigation, so a reload or a later visit resumes it.
 * - Every save sends all fields plus the step the server validates; saves run one after another (never two creates).
 * - Submit saves once more and submits; it answers the number (`SR-2026-001`). A 400 maps its field errors to the
 *   fields (`serviceRequestFieldErrorsFrom`), a 409 `ATTACHMENTS_REQUIRED_MISSING` flags the attachments step.
 */
@Injectable()
export class ServiceRequestWizardStore {
  private readonly client = inject(ServiceRequestsClient);
  private readonly categoriesClient = inject(ServiceRequestCategoriesClient);
  private readonly location = inject(Location);
  private readonly clock = inject(APP_CLOCK);
  private readonly destroyRef = inject(DestroyRef);
  private readonly links = serviceRequestsRequesterLinks(inject(SERVICE_REQUESTS_REQUESTER_BASE_LINK));
  private queue: Promise<unknown> = Promise.resolve();

  /** Today in the app time zone, read on every call (the preferred-date rule and the date input's bound). */
  readonly today = (): string => todayInTimeZone(this.clock());
  readonly form = createServiceRequestWizardForm(this.today);

  private readonly revisionState = signal(0);
  private readonly idState = signal<ServiceRequestId | null>(null);
  private readonly loadState = signal(WizardLoadState.Ready);
  private readonly loadErrorState = signal<ApiError | null>(null);
  private readonly busyCount = signal(0);
  private readonly errorState = signal<ApiError | null>(null);
  private readonly fieldErrorsState = signal<FieldErrorMap>(NO_FIELD_ERRORS);
  private readonly draftSavedState = signal(false);
  private readonly numberState = signal<string | null>(null);
  private readonly startStepState = signal(FIRST_STEP);
  private readonly categoriesState = signal<readonly ServiceRequestCategory[]>([]);
  private readonly categoriesErrorState = signal<ApiError | null>(null);
  private readonly resumedIdState = signal<ServiceRequestId | null>(null);

  /** Bumped on every form event (value, status, touched): read it in a `computed` that inspects the form. */
  readonly revision = this.revisionState.asReadonly();
  readonly id = this.idState.asReadonly();
  readonly load = this.loadState.asReadonly();
  readonly loadError = this.loadErrorState.asReadonly();
  readonly busy = computed(() => this.busyCount() > 0);
  /** The last refused save or submit. */
  readonly error = this.errorState.asReadonly();
  /** Its field errors as message keys (`serviceRequests.errors.<CODE>`), by field. */
  readonly fieldErrors = this.fieldErrorsState.asReadonly();
  /** The last refusal was the attachment interlock (the required document is missing). */
  readonly attachmentsMissing = computed(() => this.errorState()?.code === AttachmentErrorCodes.RequiredMissing);
  readonly draftSaved = this.draftSavedState.asReadonly();
  /** The allocated number once the request is submitted. */
  readonly submittedNumber = this.numberState.asReadonly();
  /** The step a resumed draft opens on. */
  readonly startStep = this.startStepState.asReadonly();
  readonly categories = this.categoriesState.asReadonly();
  readonly categoriesError = this.categoriesErrorState.asReadonly();
  /** True while the wizard resumes a saved draft (rather than starting a new one). */
  readonly resuming = computed(() => this.resumedIdState() !== null);

  constructor() {
    this.form.events.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.revisionState.update((count) => count + 1));
  }

  /** Starts the wizard: loads the categories and, for a resume, the draft `id` (null: a new request). */
  start(id: ServiceRequestId | null): void {
    this.loadCategories();
    this.resumedIdState.set(id);
    if (id !== null) this.resume(id);
  }

  /** A resume link whose id is not a request id: shown as a request that does not exist (no call is made). */
  startUnknown(): void {
    this.loadCategories();
    this.loadErrorState.set({ status: HttpStatus.notFound, code: RecordsErrorCodes.NotFound, fieldErrors: [] });
    this.loadState.set(WizardLoadState.Failed);
  }

  /** Loads the draft again after a failed resume. */
  retry(): void {
    const id = this.resumedIdState();
    if (id !== null) this.resume(id);
    if (this.categoriesErrorState() !== null) this.loadCategories();
  }

  /** Saves the draft on demand (unvalidated on the client; the server validates `step`). */
  async saveDraft(step: ServiceRequestDraftStep): Promise<boolean> {
    this.draftSavedState.set(false);
    const saved = await this.persist(step);
    this.draftSavedState.set(saved);
    return saved;
  }

  /** Saves the step the user just completed (on Next). */
  saveStep(step: ServiceRequestDraftStep): Promise<boolean> {
    this.draftSavedState.set(false);
    return this.persist(step);
  }

  /** Saves every field and submits. Resolves true once the number is allocated. */
  async submit(): Promise<boolean> {
    this.draftSavedState.set(false);
    if (!(await this.persist(ServiceRequestDraftStep.Attachments))) return false;
    const id = this.idState();
    if (id === null) return false;
    return this.run(async () => {
      const { number } = await firstValueFrom(this.client.submit(id));
      this.numberState.set(number);
    });
  }

  private resume(id: ServiceRequestId): void {
    this.loadState.set(WizardLoadState.Loading);
    this.loadErrorState.set(null);
    this.client
      .getDraft(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (draft) => {
          patchFromDraft(this.form, draft);
          this.idState.set(draft.id);
          this.startStepState.set(resumeStepIndex(draft, this.today()));
          this.loadState.set(WizardLoadState.Ready);
        },
        error: (failure: unknown) => {
          this.loadErrorState.set(recordsApiErrorFrom(failure));
          this.loadState.set(WizardLoadState.Failed);
        },
      });
  }

  private loadCategories(): void {
    this.categoriesErrorState.set(null);
    this.categoriesClient
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (categories) => this.categoriesState.set(categories),
        error: (failure: unknown) => this.categoriesErrorState.set(recordsApiErrorFrom(failure)),
      });
  }

  /** Creates the draft when there is none yet, then saves every field with `step`. Serialized behind earlier saves. */
  private persist(step: ServiceRequestDraftStep): Promise<boolean> {
    const next = this.queue.then(() =>
      this.run(async () => {
        const id = await this.ensureDraft();
        await firstValueFrom(this.client.saveDraft(id, { fields: draftFieldsOf(this.form), step }));
      }),
    );
    this.queue = next;
    return next;
  }

  private async ensureDraft(): Promise<ServiceRequestId> {
    const existing = this.idState();
    if (existing !== null) return existing;
    const { id } = await firstValueFrom(this.client.create());
    this.idState.set(id);
    this.location.replaceState(this.links.draft(id));
    return id;
  }

  /** Runs one call: busy while it runs, the refusal (with its field errors) kept when it fails. */
  private async run(call: () => Promise<void>): Promise<boolean> {
    this.busyCount.update((count) => count + 1);
    this.errorState.set(null);
    this.fieldErrorsState.set(NO_FIELD_ERRORS);
    try {
      await call();
      return true;
    } catch (failure: unknown) {
      const error = recordsApiErrorFrom(failure);
      this.errorState.set(error);
      this.fieldErrorsState.set(serviceRequestFieldErrorsFrom(error));
      return false;
    } finally {
      this.busyCount.update((count) => count - 1);
    }
  }
}
