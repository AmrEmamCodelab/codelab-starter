import { ChangeDetectionStrategy, Component, computed, inject, input, numberAttribute } from '@angular/core';
import { DgaTagComponent } from '@codelab/ng-dga';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { formatRecordInstant } from './record-instant';
import { TimelineOrder, timelineRows, type ActionLabelResolver } from './record-timeline';
import { RecordsTranslationKeys } from './records-translation-keys';
import type { TimelineEntry, WorkflowAudience } from './records.models';
import type { StatusWordingResolver } from './status-wording';

/** The heading level of the timeline's title when the screen sets none. */
export const RECORD_TIMELINE_DEFAULT_HEADING_LEVEL = 2;

/**
 * The record's history: every transition with its status (in the audience's wording), when, which side, who and why.
 * Read-only for every audience (the admin oversight shows the same timeline): nothing on it is focusable.
 *
 * Inputs: `entries` (from `RecordsClient.timeline`), `audience`, `statusKeyFor` (status key + audience -> wording key,
 * usually `statusWordingResolver('<type>.status')`), `actionLabelKeyFor` (optional; shows the action under the
 * status), `order` (newest first by default), `titleKey`, `headingLevel`.
 */
@Component({
  selector: 'app-record-timeline',
  imports: [DgaTagComponent, TranslatePipe],
  templateUrl: './record-timeline.component.html',
  styleUrl: './record-timeline.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RecordTimelineComponent {
  readonly entries = input<readonly TimelineEntry[]>([]);
  readonly audience = input.required<WorkflowAudience>();
  readonly statusKeyFor = input.required<StatusWordingResolver>();
  readonly actionLabelKeyFor = input<ActionLabelResolver | null>(null);
  readonly order = input<TimelineOrder>(TimelineOrder.NewestFirst);
  readonly titleKey = input<string>(RecordsTranslationKeys.Timeline.Title);
  readonly headingLevel = input(RECORD_TIMELINE_DEFAULT_HEADING_LEVEL, { transform: numberAttribute });

  private readonly language = inject(LanguageService);

  protected readonly keys = RecordsTranslationKeys.Timeline;
  protected readonly titleId = `app-record-timeline-${nextTimelineId()}`;
  readonly rows = computed(() => {
    const language = this.language.current();
    return timelineRows(this.entries(), {
      audience: this.audience(),
      statusKeyFor: this.statusKeyFor(),
      actionLabelKeyFor: this.actionLabelKeyFor(),
      order: this.order(),
      formatAt: (iso) => formatRecordInstant(iso, language),
    });
  });
}

let timelineCount = 0;

function nextTimelineId(): number {
  timelineCount += 1;
  return timelineCount;
}
