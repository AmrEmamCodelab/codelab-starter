import { ChangeDetectionStrategy, Component, booleanAttribute, computed, input, output } from '@angular/core';
import { DgaButtonComponent, DgaIconComponent } from '@codelab/ng-dga';
import { apiErrorMessageKey, type ApiError } from '@codelab/ng-security';
import type { TranslationParams } from '../../core/i18n/translation.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { PatternTranslationKeys } from '../pattern-translation-keys';
import { decisionButtons, type AllowedAction } from './allowed-actions';
import { StaffDetailTranslationKeys } from './staff-detail-translation-keys';

/**
 * The staff-detail page pattern (P4 pattern "staff detail + decision dialogs", page half): the record a staff member
 * acts on. A header (back link, title, status, header actions), the screen's detail cards, a history section and a
 * decision panel that renders the server-computed allowed actions. It renders state and forwards intent: the screen
 * loads the record, opens `app-decision-dialog` for an action that `requiresReason`, calls the API and
 * reloads.
 *
 * Inputs: `titleKey` + `titleParams` (the page heading, e.g. `{{reference}}`), `actions` (readonly
 * {@link AllowedAction}[] as the server sent them), `busy` (a decision is in progress: buttons disabled), `loading`,
 * `error`, `translatedCodes`, `decisionLabelKey`, `historyLabelKey` (null hides the history section).
 * Outputs: `decide` (the chosen action), `retry` (the error state's button).
 *
 * Slots: `[detailBack]` (the back link), `[detailStatus]` (a `dga-status-tag`), `[detailHeaderActions]` (print,
 * assign...), the default slot (the detail cards), `[detailHistory]` (the status tracker or a timeline).
 */
@Component({
  selector: 'app-staff-detail-pattern',
  imports: [DgaButtonComponent, DgaIconComponent, TranslatePipe],
  templateUrl: './staff-detail-pattern.component.html',
  styleUrl: './staff-detail-pattern.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StaffDetailPatternComponent {
  readonly titleKey = input.required<string>();
  readonly titleParams = input<TranslationParams | undefined>(undefined);
  readonly actions = input<readonly AllowedAction[]>([]);
  readonly busy = input(false, { transform: booleanAttribute });
  readonly loading = input(false, { transform: booleanAttribute });
  /** The failure of the last load; shows the error state instead of the record. */
  readonly error = input<ApiError | null>(null);
  readonly translatedCodes = input<ReadonlySet<string> | undefined>(undefined);
  readonly decisionLabelKey = input<string>(StaffDetailTranslationKeys.Decision);
  /** Heading of the history section; null hides the section (a record without history). */
  readonly historyLabelKey = input<string | null>(StaffDetailTranslationKeys.History);
  readonly decide = output<AllowedAction>();
  readonly retry = output<void>();

  protected readonly keys = StaffDetailTranslationKeys;
  protected readonly commonKeys = PatternTranslationKeys.Common;
  protected readonly idPrefix = `app-staff-detail-${nextDetailId()}`;
  readonly buttons = computed(() => decisionButtons(this.actions()));
  readonly errorMessageKey = computed(() => {
    const error = this.error();
    return error ? apiErrorMessageKey(error, this.translatedCodes()) : null;
  });
  readonly showRecord = computed(() => !this.loading() && this.error() === null);

  onDecide(action: AllowedAction): void {
    if (!this.busy()) {
      this.decide.emit(action);
    }
  }
}

let detailCount = 0;

function nextDetailId(): number {
  detailCount += 1;
  return detailCount;
}
