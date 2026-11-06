import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  effect,
  inject,
  input,
  numberAttribute,
  output,
  signal,
  untracked,
} from '@angular/core';
import {
  DgaAlertComponent,
  DgaButtonComponent,
  DgaConfirmDialogComponent,
  DgaConfirmDialogVariant,
  DgaTextareaComponent,
} from '@codelab/ng-dga';
import { apiErrorMessageKey, type ApiError } from '@codelab/ng-security';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationService } from '../../core/i18n/translation.service';
import {
  NEUTRAL_DECISION_TONE,
  asksForReason,
  decisionButtons,
  decisionReasonMessage,
  decisionReasonProblem,
  dialogVariantFor,
  reasonHintKey,
  reasonToSend,
  type DecisionButton,
  type DecisionToneResolver,
  type RecordDecision,
} from './decision';
import { RECORDS_TRANSLATED_CODES } from './records-error';
import { RecordsTranslationKeys } from './records-translation-keys';
import { ReasonRequirement, WORKFLOW_MAX_REASON_LENGTH, type RecordAllowedAction } from './records.models';

/** The heading level of the panel's title when the screen sets none. */
export const DECISION_PANEL_DEFAULT_HEADING_LEVEL = 2;

/**
 * The decision panel of a record: one button per SERVER-COMPUTED allowed action (`RecordDetail.allowedActions`) and
 * nothing else, so a user never sees a button the API would refuse. A terminal record has no allowed actions, so the
 * panel offers nothing (and `terminal` hides every button even if a stale list still has some).
 *
 * - An action without a reason emits `decide` at once.
 * - An action with an Optional or Required reason first opens a reason dialog (`dga-confirm-dialog` + `dga-textarea`).
 *   A blank reason is BLOCKED when it is Required; a reason longer than `maxReasonLength` is always blocked. Confirm
 *   emits `decide` with the trimmed reason (null when an optional reason was left blank).
 * - The screen sets `busy` while it calls the API (synchronously, in its `decide` handler) and `error` when the call
 *   fails. The dialog stays open while busy, shows the failure and keeps the typed reason; it closes once `busy` is
 *   false again with no `error`. Cancel closes it without emitting.
 *
 * Buttons carry `data-public-action`: the gate is the server's allowed-actions list (it already applies the caller's
 * permissions and ownership); the screen may also wrap the panel in `*hasPermission`.
 *
 * Inputs: `allowedActions`, `busy`, `terminal`, `error`, `toneFor`, `maxReasonLength`, `titleKey`, `headingLevel`,
 * `translatedCodes`. Output: `decide` ({@link RecordDecision}).
 */
@Component({
  selector: 'app-decision-panel',
  imports: [DgaAlertComponent, DgaButtonComponent, DgaConfirmDialogComponent, DgaTextareaComponent, TranslatePipe],
  templateUrl: './decision-panel.component.html',
  styleUrl: './decision-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DecisionPanelComponent {
  readonly allowedActions = input<readonly RecordAllowedAction[]>([]);
  readonly busy = input(false, { transform: booleanAttribute });
  readonly terminal = input(false, { transform: booleanAttribute });
  /** The failure of the last decision; shown in the reason dialog. */
  readonly error = input<ApiError | null>(null);
  readonly toneFor = input<DecisionToneResolver>(NEUTRAL_DECISION_TONE);
  readonly maxReasonLength = input(WORKFLOW_MAX_REASON_LENGTH, { transform: numberAttribute });
  readonly titleKey = input<string>(RecordsTranslationKeys.Decision.Title);
  readonly headingLevel = input(DECISION_PANEL_DEFAULT_HEADING_LEVEL, { transform: numberAttribute });
  readonly translatedCodes = input<ReadonlySet<string>>(RECORDS_TRANSLATED_CODES);
  readonly decide = output<RecordDecision>();

  private readonly translations = inject(TranslationService);

  protected readonly keys = RecordsTranslationKeys.Decision;
  protected readonly titleId = `app-decision-panel-${nextPanelId()}`;

  /** The action whose reason dialog is open, or null. */
  readonly pending = signal<DecisionButton | null>(null);
  readonly reason = signal('');
  /** True once the user tried to confirm; the field message shows from then on. */
  readonly showProblem = signal(false);
  /** True after the dialog emitted, until the screen's call settles. */
  private readonly awaitingResult = signal(false);

  readonly buttons = computed(() => (this.terminal() ? [] : decisionButtons(this.allowedActions(), this.toneFor())));
  readonly problem = computed(() => {
    const pending = this.pending();
    return pending ? decisionReasonProblem(pending.action.reason, this.reason(), this.maxReasonLength()) : null;
  });
  readonly problemText = computed(() => {
    const problem = this.problem();
    if (problem === null || !this.showProblem()) {
      return null;
    }
    const message = decisionReasonMessage(problem, this.maxReasonLength());
    return this.translations.translate(message.key, message.params);
  });
  readonly dialogTitleKey = computed(() => this.pending()?.action.labelKey ?? this.keys.Title);
  readonly dialogVariant = computed(() => {
    const pending = this.pending();
    return pending ? dialogVariantFor(pending.tone) : DgaConfirmDialogVariant.Default;
  });
  readonly reasonRequired = computed(() => this.pending()?.action.reason === ReasonRequirement.Required);
  readonly hintKey = computed(() => {
    const pending = this.pending();
    return pending ? reasonHintKey(pending.action.reason) : null;
  });
  readonly errorMessageKey = computed(() => {
    const error = this.error();
    return error && this.pending() ? apiErrorMessageKey(error, this.translatedCodes()) : null;
  });

  constructor() {
    effect(() => {
      const busy = this.busy();
      const error = this.error();
      untracked(() => this.settle(busy, error));
    });
  }

  /** A decision button was pressed. */
  choose(button: DecisionButton): void {
    if (this.busy()) {
      return;
    }
    if (!asksForReason(button.action)) {
      this.decide.emit({ actionKey: button.action.actionKey, reason: null });
      return;
    }
    this.reason.set('');
    this.showProblem.set(false);
    this.pending.set(button);
  }

  onReason(text: string): void {
    this.reason.set(text);
  }

  /** The dialog's Confirm: emits only a reason the action accepts. */
  confirmReason(): void {
    const pending = this.pending();
    if (pending === null || this.busy()) {
      return;
    }
    if (this.problem() !== null) {
      this.showProblem.set(true);
      return;
    }
    this.awaitingResult.set(true);
    this.decide.emit({ actionKey: pending.action.actionKey, reason: reasonToSend(this.reason()) });
  }

  /** The dialog's Cancel (or Escape): closes it, nothing is emitted. */
  cancelReason(): void {
    this.awaitingResult.set(false);
    this.pending.set(null);
  }

  private settle(busy: boolean, error: ApiError | null): void {
    if (!this.awaitingResult() || busy) {
      return;
    }
    this.awaitingResult.set(false);
    if (error === null) {
      this.pending.set(null);
    }
  }
}

let panelCount = 0;

function nextPanelId(): number {
  panelCount += 1;
  return panelCount;
}
