import {
  ChangeDetectionStrategy,
  Component,
  type OnInit,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import {
  DgaAlertComponent,
  DgaAlertVariant,
  DgaButtonComponent,
  DgaDateInputComponent,
  DgaPageFeedbackComponent,
  DgaPageFeedbackKind,
  DgaSelectComponent,
  DgaTextInputComponent,
  DgaTextareaComponent,
  type DgaPageFeedbackState,
  type DgaSelectOption,
} from '@codelab/ng-dga';
import { apiErrorMessageKey } from '@codelab/ng-security';
import { LanguageService } from '../../../../core/i18n/language.service';
import { TranslatePipe } from '../../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../../core/i18n/translation.service';
import { WizardPatternComponent, WizardStepDirective, type WizardStepChange, type WizardStepDef } from '../../../../patterns';
import { DevAutofillComponent } from '../../../../shared/dev-autofill';
import { RecordAttachmentsComponent } from '../../../../shared/record-attachments/record-attachments.component';
import {
  SERVICE_REQUEST_FIELD_LABEL_KEYS,
  SERVICE_REQUEST_STEPS,
  ServiceRequestFields,
  ServiceRequestLimits,
  ServiceRequestStepIds,
  serviceRequestAttachmentsFeedback,
  type ServiceRequestField,
  type ServiceRequestStep,
} from '../../data';
import { SERVICE_REQUESTS_REQUESTER_BASE_LINK, serviceRequestIdOf, serviceRequestsRequesterLinks } from '../requester-paths';
import { ServiceRequestsRequesterTranslationKeys } from '../requester-translation-keys';
import { categoryName } from '../service-request-categories.client';
import {
  SERVICE_REQUEST_WIZARD_FORM_KEY,
  WIZARD_FIELDS,
  controlOf,
  fieldMessageKey,
  firstStepIndexWithErrors,
  stepIsValid,
  withoutFieldErrors,
} from './service-request-wizard.form';
import { ServiceRequestWizardStore, WIZARD_TRANSLATED_CODES, WizardLoadState } from './service-request-wizard.store';

/** One row of the review on the attachments step: a field's label and the value the requester entered. */
interface ReviewRow {
  readonly field: ServiceRequestField;
  readonly labelKey: string;
  readonly value: string;
}

/**
 * `/service-requests/new` and `/service-requests/drafts/:id` (requester shell, `service-requests.create`): the
 * 3-step new-request wizard on the wizard pattern. Step 1 asks for the category and title, step 2 for the description,
 * contact mobile and preferred date, step 3 shows a review and the attachments widget (the required document) before
 * Submit. Next saves the step; Save draft saves on demand; a saved draft resumes on the first step that still needs
 * input. The submitted request's number is shown on success.
 *
 * The page renders state and forwards intent: the form, the calls and the error mapping are
 * {@link ServiceRequestWizardStore}'s; the rules are the data layer's.
 */
@Component({
  selector: 'app-service-request-wizard',
  imports: [
    DevAutofillComponent,
    DgaAlertComponent,
    DgaButtonComponent,
    DgaDateInputComponent,
    DgaPageFeedbackComponent,
    DgaSelectComponent,
    DgaTextInputComponent,
    DgaTextareaComponent,
    RecordAttachmentsComponent,
    RouterLink,
    TranslatePipe,
    WizardPatternComponent,
    WizardStepDirective,
  ],
  providers: [ServiceRequestWizardStore],
  templateUrl: './service-request-wizard.component.html',
  styleUrl: './service-request-wizard.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ServiceRequestWizardComponent implements OnInit {
  /** The draft to resume (route parameter `id`); absent for a new request. */
  readonly id = input<string | undefined>(undefined);

  protected readonly store = inject(ServiceRequestWizardStore);
  private readonly router = inject(Router);
  private readonly language = inject(LanguageService);
  private readonly translations = inject(TranslationService);
  private readonly wizard = viewChild(WizardPatternComponent);

  protected readonly keys = ServiceRequestsRequesterTranslationKeys.Wizard;
  protected readonly fields = ServiceRequestFields;
  protected readonly labelKeys = SERVICE_REQUEST_FIELD_LABEL_KEYS;
  protected readonly stepIds = ServiceRequestStepIds;
  protected readonly limits = ServiceRequestLimits;
  protected readonly loadStates = WizardLoadState;
  protected readonly alertVariants = DgaAlertVariant;
  protected readonly formKey = SERVICE_REQUEST_WIZARD_FORM_KEY;
  protected readonly translatedCodes = WIZARD_TRANSLATED_CODES;
  protected readonly links = serviceRequestsRequesterLinks(inject(SERVICE_REQUESTS_REQUESTER_BASE_LINK));

  /** The current step (two-way with the pattern). */
  readonly step = signal(0);

  readonly steps: readonly WizardStepDef[] = SERVICE_REQUEST_STEPS.map((definition) => ({
    id: definition.id,
    titleKey: definition.titleKey,
    descriptionKey: definition.descriptionKey,
    valid: computed(() => {
      this.store.revision();
      return stepIsValid(this.store.form, definition);
    }),
  }));
  readonly titleKey = computed(() => (this.store.resuming() ? this.keys.TitleResume : this.keys.TitleNew));
  readonly patternError = computed(() => withoutFieldErrors(this.store.error()));
  readonly loadErrorKey = computed(() => {
    const error = this.store.loadError();
    return error ? apiErrorMessageKey(error, this.translatedCodes) : null;
  });
  readonly categoryOptions = computed<readonly DgaSelectOption<string>[]>(() => {
    const language = this.language.current();
    return this.store.categories().map((category) => ({ value: category.id, label: categoryName(category, language) }));
  });
  readonly draftSavedFeedback = computed<DgaPageFeedbackState | null>(() =>
    this.store.draftSaved() ? { kind: DgaPageFeedbackKind.Success, message: this.translations.translate(this.keys.DraftSaved) } : null,
  );
  /** The error shown on the attachments step, in place of the widget, when the draft's attachments id could not be read. */
  readonly attachmentsFeedback = computed(() =>
    serviceRequestAttachmentsFeedback(this.store.attachmentsError(), (key, params) => this.translations.translate(key, params)),
  );
  readonly reviewRows = computed<readonly ReviewRow[]>(() => {
    this.store.revision();
    return WIZARD_FIELDS.map((field) => ({ field, labelKey: SERVICE_REQUEST_FIELD_LABEL_KEYS[field], value: this.displayValue(field) }));
  });

  constructor() {
    // A resumed draft opens on the first step that still needs input.
    effect(() => {
      const start = this.store.startStep();
      untracked(() => this.step.set(start));
    });
  }

  ngOnInit(): void {
    const raw = this.id();
    if (raw === undefined) {
      this.store.start(null);
      return;
    }
    const id = serviceRequestIdOf(raw);
    if (id === null) this.store.startUnknown();
    else this.store.start(id);
  }

  valueOf(field: ServiceRequestField): string {
    this.store.revision();
    return controlOf(this.store.form, field).value ?? '';
  }

  categoryValue(): string | null {
    this.store.revision();
    return controlOf(this.store.form, ServiceRequestFields.CategoryId).value;
  }

  setValue(field: ServiceRequestField, value: string | null): void {
    const control = controlOf(this.store.form, field);
    control.setValue(value === '' ? null : value);
    control.markAsDirty();
  }

  touch(field: ServiceRequestField, touched: boolean): void {
    if (touched) controlOf(this.store.form, field).markAsTouched();
  }

  /** The translated message under a field, or null. After a refused Next every message of the step shows. */
  errorText(field: ServiceRequestField): string | null {
    this.store.revision();
    const key = fieldMessageKey(this.store.form, field, {
      serverErrors: this.store.fieldErrors(),
      revealAll: this.wizard()?.showStepRefusal() ?? false,
    });
    return key === null ? null : this.translations.translate(key);
  }

  onNext(change: WizardStepChange): void {
    const step = this.stepById(change.fromId);
    if (step) void this.store.saveStep(step.draftStep).then((saved) => this.afterRefusal(saved));
  }

  onSaveDraft(stepId: string): void {
    const step = this.stepById(stepId);
    if (step) void this.store.saveDraft(step.draftStep).then((saved) => this.afterRefusal(saved));
  }

  onSubmit(): void {
    void this.store.submit().then((submitted) => this.afterRefusal(submitted));
  }

  leave(): void {
    void this.router.navigateByUrl(this.links.mine);
  }

  retry(): void {
    this.store.retry();
  }

  /** A refusal with field errors takes the user to the first step that owns one; the interlock to the attachments. */
  private afterRefusal(succeeded: boolean): void {
    if (succeeded) return;
    const index = this.store.attachmentsMissing()
      ? SERVICE_REQUEST_STEPS.findIndex((step) => step.id === ServiceRequestStepIds.Attachments)
      : firstStepIndexWithErrors(this.store.fieldErrors());
    if (index !== null && index >= 0) this.step.set(index);
  }

  private stepById(id: string): ServiceRequestStep | undefined {
    return SERVICE_REQUEST_STEPS.find((step) => step.id === id);
  }

  private displayValue(field: ServiceRequestField): string {
    const value = controlOf(this.store.form, field).value ?? '';
    if (field !== ServiceRequestFields.CategoryId) return value;
    const category = this.store.categories().find((candidate) => candidate.id === value);
    return category ? categoryName(category, this.language.current()) : '';
  }
}
