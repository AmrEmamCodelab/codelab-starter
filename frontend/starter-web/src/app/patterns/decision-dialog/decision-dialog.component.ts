import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  input,
  linkedSignal,
  model,
  numberAttribute,
  output,
  viewChild,
  type ElementRef,
} from '@angular/core';
import type { ApiError } from '@codelab/ng-security';
import type { TranslationParams } from '../../core/i18n/translation.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { EditorDialogPatternComponent } from '../editor-dialog/editor-dialog-pattern.component';
import { NO_FIELD_ERRORS, fieldErrorsFrom, firstFieldError, hasFieldErrors, type FieldErrorMap } from '../editor-dialog/field-errors';
import { PatternTranslationKeys } from '../pattern-translation-keys';
import { DecisionDialogTranslationKeys } from './decision-dialog-translation-keys';
import {
  DECISION_REASON_FIELD,
  DECISION_REASON_MAX_LENGTH,
  DECISION_REASON_MIN_LENGTH,
  normalizeReason,
  reasonProblem,
  reasonProblemMessage,
  type ReasonLimits,
} from './decision-reason';

/** A message the reason field shows: a translation key and its parameters. */
interface FieldMessage {
  readonly key: string;
  readonly params?: TranslationParams;
}

/**
 * The decision dialog (P4 pattern "staff detail + decision dialogs", dialog half): the reason a staff member gives
 * for a decision that `requiresReason` (reject, return, cancel). Built on {@link EditorDialogPatternComponent}, so it is
 * CONTROLLED the same way: `open` shows it, and the screen sets `open` back to false after `cancel` or after the API
 * accepted the decision.
 *
 * - The reason is mandatory, between `minLength` and `maxLength` characters after trimming (defaults
 *   {@link DECISION_REASON_MIN_LENGTH} and {@link DECISION_REASON_MAX_LENGTH}; keep them equal to the server validator).
 * - `confirm` emits the trimmed reason only when it is valid; otherwise the field shows why and takes focus. The
 *   message appears after the first confirm or when the field loses focus, never while the user starts typing.
 * - `error` (the failed decision) shows in the dialog's alert; a server message for the `reasonField` (default
 *   {@link DECISION_REASON_FIELD}) also shows under the field.
 *
 * Inputs: `open`, `titleKey`, `messageKey` + `messageParams` (an optional explanation above the field),
 * `reasonLabelKey`, `confirmKey`, `cancelKey`, `busy`, `destructive`, `error`, `translatedCodes`, `fieldErrors`,
 * `minLength`, `maxLength`, `reasonField`. Model: `reason`. Outputs: `confirm` (the trimmed reason), `cancel`.
 */
@Component({
  selector: 'app-decision-dialog',
  imports: [EditorDialogPatternComponent, TranslatePipe],
  templateUrl: './decision-dialog.component.html',
  styleUrl: './decision-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DecisionDialogComponent {
  readonly open = input(false, { transform: booleanAttribute });
  readonly titleKey = input.required<string>();
  readonly messageKey = input<string | null>(null);
  readonly messageParams = input<TranslationParams | undefined>(undefined);
  readonly reasonLabelKey = input<string>(DecisionDialogTranslationKeys.Reason);
  readonly confirmKey = input<string>(DecisionDialogTranslationKeys.Confirm);
  readonly cancelKey = input<string>(PatternTranslationKeys.Dialog.Cancel);
  readonly busy = input(false, { transform: booleanAttribute });
  readonly destructive = input(false, { transform: booleanAttribute });
  readonly error = input<ApiError | null>(null);
  readonly translatedCodes = input<ReadonlySet<string> | undefined>(undefined);
  readonly fieldErrors = input<FieldErrorMap>(NO_FIELD_ERRORS);
  readonly minLength = input(DECISION_REASON_MIN_LENGTH, { transform: numberAttribute });
  readonly maxLength = input(DECISION_REASON_MAX_LENGTH, { transform: numberAttribute });
  readonly reasonField = input(DECISION_REASON_FIELD);
  readonly reason = model('');
  readonly confirm = output<string>();
  readonly cancel = output<void>();

  private readonly reasonInput = viewChild.required<ElementRef<HTMLTextAreaElement>>('reasonInput');

  protected readonly keys = DecisionDialogTranslationKeys;
  protected readonly fieldId = `app-decision-dialog-reason-${nextDecisionDialogId()}`;
  protected readonly errorId = `${this.fieldId}-error`;
  protected readonly counterId = `${this.fieldId}-counter`;
  /** True once the user tried to confirm or left the field; reset whenever the dialog opens or closes. */
  readonly showProblems = linkedSignal({ source: this.open, computation: () => false });
  readonly limits = computed<ReasonLimits>(() => ({ minLength: this.minLength(), maxLength: this.maxLength() }));
  readonly problem = computed(() => reasonProblem(this.reason(), this.limits()));
  readonly fieldMessage = computed<FieldMessage | null>(() => {
    const problem = this.problem();
    if (problem !== null && this.showProblems()) {
      return reasonProblemMessage(problem, this.limits());
    }
    const serverKey = firstFieldError(this.serverFieldErrors(), this.reasonField());
    return serverKey ? { key: serverKey } : null;
  });
  readonly describedBy = computed(() => (this.fieldMessage() ? `${this.errorId} ${this.counterId}` : this.counterId));
  private readonly serverFieldErrors = computed(() =>
    hasFieldErrors(this.fieldErrors()) ? this.fieldErrors() : fieldErrorsFrom(this.error()));

  onInput(event: Event): void {
    this.reason.set((event.target as HTMLTextAreaElement).value);
  }

  onBlur(): void {
    this.showProblems.set(true);
  }

  onConfirm(): void {
    if (this.problem() !== null) {
      this.showProblems.set(true);
      this.reasonInput().nativeElement.focus();
      return;
    }
    this.confirm.emit(normalizeReason(this.reason()));
  }
}

let decisionDialogCount = 0;

function nextDecisionDialogId(): number {
  decisionDialogCount += 1;
  return decisionDialogCount;
}
