import {
  ChangeDetectionStrategy,
  Component,
  type ElementRef,
  afterRenderEffect,
  booleanAttribute,
  computed,
  contentChild,
  contentChildren,
  effect,
  inject,
  input,
  model,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { DgaButtonComponent, DgaIconComponent, DgaStepperComponent, DgaStepperOrientation, type DgaStep } from '@codelab/ng-dga';
import { apiErrorMessageKey, type ApiError } from '@codelab/ng-security';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationService } from '../../core/i18n/translation.service';
import { PatternTranslationKeys } from '../pattern-translation-keys';
import { allFieldErrors, fieldErrorsFrom, hasFieldErrors } from '../editor-dialog/field-errors';
import { errorsByStep, errorsOfStep, firstStepWithErrors, type WizardFieldSteps } from './wizard-errors';
import {
  WIZARD_REVIEW_STEP_ID,
  clampWizardStep,
  guardNext,
  guardStepChange,
  guardSubmit,
  type WizardGuardResult,
  type WizardPosition,
  type WizardStepDef,
} from './wizard-navigation';
import { WizardReviewDirective, WizardStepDirective, type WizardStepContext } from './wizard-templates.directive';
import { WizardTranslationKeys } from './wizard-translation-keys';

/** What `next` and `back` report: the step the user left and the one they moved to. */
export interface WizardStepChange {
  readonly fromId: string;
  readonly toId: string;
}

/** The review step is a read-only summary: it never blocks Submit by itself. */
const REVIEW_ALWAYS_VALID = signal(true).asReadonly();
const NO_FIELD_STEPS: WizardFieldSteps = Object.freeze({});
const FIRST_STEP = 0;
const STEP_NUMBER_OFFSET = 1;

/**
 * The multi-step wizard page pattern (P4 pattern "multi-step wizard"): a step trail (`dga-stepper`, navigable), the
 * current step's body, a review step slot and the Back / Next / Save draft / Submit bar. The screen owns the form,
 * the API calls and the permission gates; the pattern owns step navigation and its guard.
 *
 * - Steps: `steps` (`WizardStepDef[]`: id, titleKey, descriptionKey?, `valid` signal) plus one
 *   `<ng-template wizardStep="id" let-errors="errors">` per step. `<ng-template wizardReview>` appends a review step.
 * - `[(step)]` is the current index. Next validates the current step (refused: the step is flagged and a message
 *   shows); Back never validates; the trail lets the user revisit earlier or completed steps (linear, the default) or
 *   jump anywhere (`linear` false). Submit validates every step and moves to the first invalid one.
 * - `error` (the failed save or submit, an `ApiError`): its message and reference code show in an alert; its
 *   problem+json field errors are mapped to steps through `fieldSteps` (path prefix -> step id), the wizard moves to
 *   the first step with errors, flags every such step in the trail and passes each step its own errors (`let-errors`).
 * - While `busy` every action is refused and Submit shows a spinner.
 *
 * Outputs: `next` / `back` ({ fromId, toId }, after the guard allowed the move), `saveDraft` (current step id; never
 * validated), `submit`, `cancel` (only with `cancellable`).
 */
@Component({
  selector: 'app-wizard-pattern',
  imports: [DgaButtonComponent, DgaIconComponent, DgaStepperComponent, NgTemplateOutlet, TranslatePipe],
  templateUrl: './wizard-pattern.component.html',
  styleUrl: './wizard-pattern.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[attr.data-orientation]': 'orientation()' },
})
export class WizardPatternComponent {
  readonly steps = input.required<readonly WizardStepDef[]>();
  /** Zero-based index of the current step (two-way). Out-of-range values are clamped. */
  readonly step = model(FIRST_STEP);
  readonly linear = input(true, { transform: booleanAttribute });
  /** Horizontal trail above the body, or vertical trail beside it (stacked on narrow screens). */
  readonly orientation = input<DgaStepperOrientation>(DgaStepperOrientation.Horizontal);
  /** Translation key of the trail's accessible name. */
  readonly labelKey = input<string>(WizardTranslationKeys.Progress);
  readonly reviewTitleKey = input<string>(WizardTranslationKeys.Review);
  readonly submitKey = input<string>(WizardTranslationKeys.Submit);
  /** Shows Save draft (the screen gates it: feature switch and permission). */
  readonly draftEnabled = input(false, { transform: booleanAttribute });
  /** Shows Cancel. */
  readonly cancellable = input(false, { transform: booleanAttribute });
  readonly busy = input(false, { transform: booleanAttribute });
  /** The failure of the last save or submit. */
  readonly error = input<ApiError | null>(null);
  /** The codes this screen has `errors.<CODE>` text for (default: the platform codes of ng-security). */
  readonly translatedCodes = input<ReadonlySet<string> | undefined>(undefined);
  /** Server field path (or prefix) to the id of the step that owns it. */
  readonly fieldSteps = input<WizardFieldSteps>(NO_FIELD_STEPS);

  readonly next = output<WizardStepChange>();
  readonly back = output<WizardStepChange>();
  readonly saveDraft = output<string>();
  readonly submit = output<void>();
  readonly cancel = output<void>();

  private readonly translations = inject(TranslationService);
  private readonly stepTemplates = contentChildren(WizardStepDirective);
  private readonly reviewTemplate = contentChild(WizardReviewDirective);
  private readonly heading = viewChild.required<ElementRef<HTMLElement>>('stepHeading');
  private readonly furthestVisited = signal(FIRST_STEP);
  private readonly refusedSteps = signal<ReadonlySet<number>>(new Set());
  private focusHeadingPending = false;

  protected readonly keys = WizardTranslationKeys;
  protected readonly commonKeys = PatternTranslationKeys.Common;

  readonly allSteps = computed<readonly WizardStepDef[]>(() => {
    const steps = this.steps();
    if (!this.reviewTemplate()) return steps;
    return [...steps, { id: WIZARD_REVIEW_STEP_ID, titleKey: this.reviewTitleKey(), valid: REVIEW_ALWAYS_VALID }];
  });
  readonly stepIds = computed(() => this.allSteps().map((definition) => definition.id));
  readonly validity = computed(() => this.allSteps().map((definition) => definition.valid()));
  readonly current = computed(() => clampWizardStep({ step: this.step(), count: this.allSteps().length }));
  readonly currentDef = computed(() => this.allSteps()[this.current()]);
  readonly isFirst = computed(() => this.current() === FIRST_STEP);
  readonly isLast = computed(() => this.current() === this.allSteps().length - STEP_NUMBER_OFFSET);
  readonly stepNumber = computed(() => ({ current: this.current() + STEP_NUMBER_OFFSET, total: this.allSteps().length }));

  readonly stepErrors = computed(() => errorsByStep(fieldErrorsFrom(this.error()), this.fieldSteps(), this.stepIds()));
  readonly errorMessageKey = computed(() => {
    const error = this.error();
    return error ? apiErrorMessageKey(error, this.translatedCodes()) : null;
  });
  readonly currentFieldErrors = computed(() => allFieldErrors(errorsOfStep(this.stepErrors(), this.currentDef().id)));
  readonly unmappedFieldErrors = computed(() => allFieldErrors(this.stepErrors().unmapped));
  /** Other steps that hold server errors, for the summary's "go to step" buttons. */
  readonly otherStepsWithErrors = computed(() =>
    this.allSteps()
      .map((definition, index) => ({ definition, index }))
      .filter(({ definition, index }) => index !== this.current() && this.hasServerErrors(definition.id)));
  readonly showSummary = computed(
    () =>
      this.errorMessageKey() !== null ||
      this.currentFieldErrors().length > 0 ||
      this.unmappedFieldErrors().length > 0 ||
      this.otherStepsWithErrors().length > 0,
  );
  /** Next or Submit was refused on the current step and it is still invalid. */
  readonly showStepRefusal = computed(() => this.refusedSteps().has(this.current()) && !this.validity()[this.current()]);

  readonly stepperSteps = computed<readonly DgaStep[]>(() => {
    const reached = Math.max(this.furthestVisited(), this.current());
    const validity = this.validity();
    const refused = this.refusedSteps();
    return this.allSteps().map((definition, index) => ({
      label: this.translations.translate(definition.titleKey),
      description: definition.descriptionKey ? this.translations.translate(definition.descriptionKey) : undefined,
      completed: index <= reached && validity[index],
      invalid: this.hasServerErrors(definition.id) || (refused.has(index) && !validity[index]),
    }));
  });

  readonly currentTemplate = computed(() => {
    const id = this.currentDef().id;
    const review = this.reviewTemplate();
    if (review && id === WIZARD_REVIEW_STEP_ID) return review.template;
    return this.stepTemplates().find((template) => template.id() === id)?.template ?? null;
  });
  readonly currentContext = computed<WizardStepContext>(() => ({
    $implicit: this.currentDef().id,
    errors: errorsOfStep(this.stepErrors(), this.currentDef().id),
  }));

  constructor() {
    // Every step the user has reached (by the buttons, the trail or the screen setting `step`) counts as visited.
    effect(() => {
      const current = this.current();
      untracked(() => this.furthestVisited.update((furthest) => Math.max(furthest, current)));
    });
    // A new server error with field errors takes the user to the first step that owns one.
    effect(() => {
      const errors = this.stepErrors();
      untracked(() => {
        const index = firstStepWithErrors(this.stepIds(), errors);
        if (index !== null && index !== this.current()) this.moveTo(index);
      });
    });
    afterRenderEffect({
      write: () => {
        this.current();
        untracked(() => this.focusHeadingIfPending());
      },
    });
  }

  goNext(): void {
    if (this.busy()) return;
    const from = this.current();
    if (!this.allowed(guardNext(this.position()))) return;
    this.next.emit(this.changeOf({ from, to: from + STEP_NUMBER_OFFSET }));
    this.moveTo(from + STEP_NUMBER_OFFSET);
  }

  goBack(): void {
    if (this.busy() || this.isFirst()) return;
    const from = this.current();
    this.back.emit(this.changeOf({ from, to: from - STEP_NUMBER_OFFSET }));
    this.moveTo(from - STEP_NUMBER_OFFSET);
  }

  /** A step chosen in the trail or in the error summary. */
  goTo(index: number): void {
    if (this.busy() || index === this.current()) return;
    if (this.allowed(guardStepChange(this.position(), index, this.linear()))) this.moveTo(index);
  }

  requestSubmit(): void {
    if (this.busy()) return;
    if (this.allowed(guardSubmit(this.validity()))) this.submit.emit();
  }

  requestSaveDraft(): void {
    if (!this.busy()) this.saveDraft.emit(this.currentDef().id);
  }

  requestCancel(): void {
    if (!this.busy()) this.cancel.emit();
  }

  private hasServerErrors(stepId: string): boolean {
    return hasFieldErrors(errorsOfStep(this.stepErrors(), stepId));
  }

  private position(): WizardPosition {
    return { validity: this.validity(), current: this.current() };
  }

  /** Applies a guard: a refusal flags the blocking step and takes the user there. */
  private allowed(result: WizardGuardResult): boolean {
    if (result.allowed) return true;
    const blockedAt = result.blockedAt;
    if (blockedAt !== null) {
      this.refusedSteps.update((steps) => new Set(steps).add(blockedAt));
      if (blockedAt !== this.current()) this.moveTo(blockedAt);
    }
    return false;
  }

  private changeOf({ from, to }: { readonly from: number; readonly to: number }): WizardStepChange {
    const ids = this.stepIds();
    return { fromId: ids[from], toId: ids[to] };
  }

  private moveTo(index: number): void {
    this.step.set(index);
    this.focusHeadingPending = true;
  }

  /** After a step change the user made, focus moves to the new step's heading so screen readers announce it. */
  private focusHeadingIfPending(): void {
    if (!this.focusHeadingPending) return;
    this.focusHeadingPending = false;
    this.heading().nativeElement.focus();
  }
}
