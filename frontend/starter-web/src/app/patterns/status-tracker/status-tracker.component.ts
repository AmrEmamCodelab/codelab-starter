import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { DgaStatusTagComponent } from '@codelab/ng-dga';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import {
  HistoryOrder,
  NEUTRAL_STATUS_TONE,
  formatHistoryInstant,
  latestEntry,
  sortHistory,
  type StatusHistoryEntry,
  type StatusToneResolver,
} from './status-history';
import { StatusTrackerTranslationKeys } from './status-tracker-translation-keys';

/** The heading level of the tracker's title: 2 on its own page area, 3 inside a page section. */
export const StatusTrackerHeadingLevel = {
  Page: 2,
  Section: 3,
} as const;
export type StatusTrackerHeadingLevel = (typeof StatusTrackerHeadingLevel)[keyof typeof StatusTrackerHeadingLevel];

/**
 * The status tracker (part of the P4 pattern "requester detail + status tracker"): a current-status card (status tag,
 * since when, the reason when one was given) above a timeline of every status change (status, date and time, who,
 * why). Read-only: no actions, nothing focusable. The screen maps its API history to `StatusHistoryEntry` items and
 * its statuses to tones; the pattern sorts, picks the current entry and formats dates in the app language.
 *
 * Inputs: `entries`, `titleKey` (default "Request status"), `toneFor` (status key -> `DgaStatusTone`, default
 * neutral), `order` (`HistoryOrder`, default newest first), `current` (override the current entry when the record's
 * status is not the latest history entry), `headingLevel` (2 by default; 3 inside a page section).
 */
@Component({
  selector: 'app-status-tracker',
  imports: [DgaStatusTagComponent, TranslatePipe],
  templateUrl: './status-tracker.component.html',
  styleUrl: './status-tracker.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatusTrackerComponent {
  readonly entries = input<readonly StatusHistoryEntry[]>([]);
  readonly titleKey = input<string>(StatusTrackerTranslationKeys.Title);
  readonly toneFor = input<StatusToneResolver>(NEUTRAL_STATUS_TONE);
  readonly order = input<HistoryOrder>(HistoryOrder.NewestFirst);
  readonly current = input<StatusHistoryEntry | null>(null);
  readonly headingLevel = input<StatusTrackerHeadingLevel>(StatusTrackerHeadingLevel.Page);

  private readonly language = inject(LanguageService);

  protected readonly keys = StatusTrackerTranslationKeys;
  protected readonly titleId = `app-status-tracker-title-${nextTrackerId()}`;
  readonly timeline = computed(() => sortHistory(this.entries(), this.order()));
  readonly currentEntry = computed(() => this.current() ?? latestEntry(this.entries()));
  /** The sub-heading level below the tracker's own heading. */
  readonly subHeadingLevel = computed(() => this.headingLevel() + 1);

  /** Local date and time text of an entry, in the app language. */
  formatAt(entry: StatusHistoryEntry): string {
    return formatHistoryInstant(entry.at, this.language.current());
  }
}

let trackerCount = 0;

function nextTrackerId(): number {
  trackerCount += 1;
  return trackerCount;
}
