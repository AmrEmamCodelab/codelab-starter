import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  DgaAlertComponent,
  DgaAlertVariant,
  DgaInfoFieldComponent,
  DgaSelectComponent,
  DgaTextareaComponent,
  DgaTextInputComponent,
} from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationService } from '../../core/i18n/translation.service';
import {
  WizardPatternComponent,
  WizardReviewDirective,
  WizardStepDirective,
  type WizardFieldSteps,
  type WizardStepDef,
} from '../../patterns';
import { ShowcaseFrameComponent } from '../frame/showcase-frame.component';
import { SHOWCASE_API_ERROR, ShowcaseState } from '../frame/showcase-state';
import { ShowcaseStateSwitchComponent } from '../frame/showcase-state-switch.component';
import { ShowcasePageId } from '../showcase-pages';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';
import { CITY_FIXTURE, NOTES_MAX_LENGTH, withLabels } from '../components/component-fixtures';
import { STAFF_DETAIL_STATES } from './staff-detail-page.component';

const P = ShowcaseTranslationKeys.Patterns;

/** The step ids of the wizard demo. */
export const WizardDemoStep = { Applicant: 'applicant', Request: 'request', Notes: 'notes' } as const;

/** Server field paths (prefixes) to the step that owns them. */
export const WIZARD_FIELD_STEPS: WizardFieldSteps = {
  applicant: WizardDemoStep.Applicant,
  request: WizardDemoStep.Request,
  notes: WizardDemoStep.Notes,
};

/** What the wizard demo reports after Submit or Save draft. */
export const WizardOutcome = { Submitted: 'submitted', DraftSaved: 'draft-saved' } as const;

export type WizardOutcome = (typeof WizardOutcome)[keyof typeof WizardOutcome];

const OUTCOME_KEYS: Readonly<Record<WizardOutcome, string>> = {
  [WizardOutcome.Submitted]: P.WizardSubmitted,
  [WizardOutcome.DraftSaved]: P.WizardDraftSaved,
};

/**
 * The multi-step wizard pattern: three steps with their own validity (Next refuses an incomplete step), a review step,
 * Save draft and Cancel. The state switch shows the busy state (Loading) and a failed submit (Error).
 */
@Component({
  selector: 'app-showcase-wizard-page',
  imports: [
    DgaAlertComponent,
    DgaInfoFieldComponent,
    DgaSelectComponent,
    DgaTextareaComponent,
    DgaTextInputComponent,
    ShowcaseFrameComponent,
    ShowcaseStateSwitchComponent,
    TranslatePipe,
    WizardPatternComponent,
    WizardReviewDirective,
    WizardStepDirective,
  ],
  templateUrl: './wizard-page.component.html',
  styleUrl: './pattern-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WizardPageComponent {
  private readonly translations = inject(TranslationService);

  protected readonly pageId = ShowcasePageId.Wizard;
  protected readonly keys = ShowcaseTranslationKeys;
  protected readonly states = STAFF_DETAIL_STATES;
  protected readonly Step = WizardDemoStep;
  protected readonly Outcome = WizardOutcome;
  protected readonly fieldSteps = WIZARD_FIELD_STEPS;
  protected readonly notesMax = NOTES_MAX_LENGTH;
  protected readonly AlertVariant = DgaAlertVariant;
  protected readonly cities = computed(() => withLabels(CITY_FIXTURE, key => this.translations.translate(key)));

  readonly state = signal<ShowcaseState>(ShowcaseState.Ready);
  readonly step = signal(0);
  readonly name = signal('');
  readonly email = signal('');
  readonly city = signal<string | null>(null);
  readonly notes = signal('');
  readonly outcome = signal<WizardOutcome | null>(null);

  protected readonly steps: readonly WizardStepDef[] = [
    {
      id: WizardDemoStep.Applicant,
      titleKey: P.WizardStepApplicant,
      valid: computed(() => this.name().trim() !== ''),
    },
    {
      id: WizardDemoStep.Request,
      titleKey: P.WizardStepRequest,
      descriptionKey: P.WizardStepRequestHint,
      valid: computed(() => this.city() !== null),
    },
    { id: WizardDemoStep.Notes, titleKey: P.WizardStepNotes, valid: computed(() => true) },
  ];
  protected readonly busy = computed(() => this.state() === ShowcaseState.Loading);
  protected readonly error = computed(() => (this.state() === ShowcaseState.Error ? SHOWCASE_API_ERROR : null));
  protected readonly outcomeKey = computed(() => {
    const outcome = this.outcome();
    return outcome === null ? null : OUTCOME_KEYS[outcome];
  });
  protected readonly cityLabel = computed(() => this.cities().find(option => option.value === this.city())?.label ?? '');

  record(outcome: WizardOutcome): void {
    this.outcome.set(outcome);
  }

  /** Cancel starts the demo over: empty fields, first step. */
  restart(): void {
    this.name.set('');
    this.email.set('');
    this.city.set(null);
    this.notes.set('');
    this.outcome.set(null);
    this.step.set(0);
  }
}
