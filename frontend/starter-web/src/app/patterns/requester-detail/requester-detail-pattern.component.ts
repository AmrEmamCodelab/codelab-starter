import { ChangeDetectionStrategy, Component, booleanAttribute, computed, contentChild, inject, input, output } from '@angular/core';
import { DgaButtonComponent, DgaIconComponent, DgaStatusTagComponent, type DgaStatusTone } from '@codelab/ng-dga';
import { apiErrorMessageKey, type ApiError } from '@codelab/ng-security';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { PatternTranslationKeys } from '../pattern-translation-keys';
import { NEUTRAL_STATUS_TONE, formatHistoryInstant, type StatusHistoryEntry, type StatusToneResolver } from '../status-tracker/status-history';
import { StatusTrackerComponent, StatusTrackerHeadingLevel } from '../status-tracker/status-tracker.component';
import { StatusTrackerTranslationKeys } from '../status-tracker/status-tracker-translation-keys';
import { DetailViewState, detailViewState } from './requester-detail-state';
import { RequesterDetailActionsDirective, RequesterDetailAttachmentsDirective } from './requester-detail-slots.directive';
import { RequesterDetailTranslationKeys } from './requester-detail-translation-keys';

/**
 * The requester detail page pattern (P4 pattern "requester detail + status tracker"): the page a requester sees for
 * one of their records. A header (title, record number, created on, status tag), the screen's summary cards, an
 * attachments section (P6), and beside them the status tracker and the screen's actions. Loading and error (with
 * reference code and Retry) states replace the body.
 *
 * The screen owns the record, the API calls and every gate: it projects only the actions the user may take
 * (`*hasPermission`) and disables those the status does not allow.
 *
 * Inputs: `titleKey`, `recordNumber`, `statusKey` (translation key of the record's status), `statusTone`, `createdAt`
 * (ISO), `history` (`StatusHistoryEntry[]` for the tracker), `toneFor` (tracker tones), `trackerTitleKey`, `loading`,
 * `error`, `translatedCodes`. Output: `retry`.
 *
 * Slots: `[detailBanner]` (a decision banner above the layout, such as "returned for changes"), `[detailSummary]`
 * (summary cards), `[detailAttachments]` (attachments section), `[detailActions]` (the actions group).
 */
@Component({
  selector: 'app-requester-detail-pattern',
  imports: [DgaButtonComponent, DgaIconComponent, DgaStatusTagComponent, StatusTrackerComponent, TranslatePipe],
  templateUrl: './requester-detail-pattern.component.html',
  styleUrl: './requester-detail-pattern.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RequesterDetailPatternComponent {
  /** Translation key of the page title. */
  readonly titleKey = input.required<string>();
  /** The record's human-readable number (data, shown as is). */
  readonly recordNumber = input<string | null>(null);
  /** Translation key of the record's current status. */
  readonly statusKey = input<string | null>(null);
  readonly statusTone = input<DgaStatusTone>('neutral');
  /** When the record was created, ISO-8601. */
  readonly createdAt = input<string | null>(null);
  readonly history = input<readonly StatusHistoryEntry[]>([]);
  readonly toneFor = input<StatusToneResolver>(NEUTRAL_STATUS_TONE);
  /** Translation key of the tracker heading (default: the tracker's own "Request status"). */
  readonly trackerTitleKey = input<string>(StatusTrackerTranslationKeys.Title);
  readonly loading = input(false, { transform: booleanAttribute });
  /** The failure of the last load. */
  readonly error = input<ApiError | null>(null);
  readonly translatedCodes = input<ReadonlySet<string> | undefined>(undefined);
  readonly retry = output<void>();

  private readonly language = inject(LanguageService);
  private readonly attachments = contentChild(RequesterDetailAttachmentsDirective);
  private readonly actions = contentChild(RequesterDetailActionsDirective);

  protected readonly keys = RequesterDetailTranslationKeys;
  protected readonly commonKeys = PatternTranslationKeys.Common;
  protected readonly states = DetailViewState;
  protected readonly trackerHeadingLevel = StatusTrackerHeadingLevel.Page;
  protected readonly titleId = `app-requester-detail-title-${nextDetailId()}`;

  readonly viewState = computed(() => detailViewState({ loading: this.loading(), error: this.error() }));
  readonly hasAttachments = computed(() => this.attachments() !== undefined);
  readonly hasActions = computed(() => this.actions() !== undefined);
  readonly errorMessageKey = computed(() => {
    const error = this.error();
    return error ? apiErrorMessageKey(error, this.translatedCodes()) : null;
  });
  readonly createdOnText = computed(() => {
    const createdAt = this.createdAt();
    return createdAt ? formatHistoryInstant(createdAt, this.language.current()) : null;
  });
}

let detailCount = 0;

function nextDetailId(): number {
  detailCount += 1;
  return detailCount;
}
