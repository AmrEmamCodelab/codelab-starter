import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Visual tones of a status pill. The app maps each of its statuses (from the workflow catalog) to a tone; the
 * library never knows status names. `progress` is the brand tone for work in progress.
 */
export type DgaStatusTone = 'neutral' | 'info' | 'warning' | 'success' | 'error' | 'progress';

/**
 * The DGA status tag: a solid pill with a leading dot for a record's status. The status text is projected content
 * (already translated); the dot is decorative. Colours come only from the tone tokens.
 *
 * @example
 * <dga-status-tag [tone]="statusTone(record.statusId)">{{ record.statusLabelKey | translate }}</dga-status-tag>
 */
@Component({
  selector: 'dga-status-tag',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-status-tag.component.html',
  styleUrl: './dga-status-tag.component.css',
})
export class DgaStatusTagComponent {
  readonly tone = input<DgaStatusTone>('neutral');
}
