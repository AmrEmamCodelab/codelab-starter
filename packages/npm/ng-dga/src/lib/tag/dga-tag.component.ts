import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { DgaIconComponent } from '../icon/dga-icon.component';
import type { DgaIconName } from '../icon/dga-icon-names.generated';

export type DgaTagTone = 'neutral' | 'success' | 'info' | 'warning' | 'error';

/**
 * The DGA tag: a light semantic chip. The text is projected content (already translated); the optional icon is
 * decorative, so the text alone must carry the meaning.
 *
 * @example
 * <dga-tag tone="success" icon="check">{{ 'records.verified' | translate }}</dga-tag>
 */
@Component({
  selector: 'dga-tag',
  imports: [DgaIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-tag.component.html',
  styleUrl: './dga-tag.component.css',
})
export class DgaTagComponent {
  readonly tone = input<DgaTagTone>('neutral');
  readonly icon = input<DgaIconName | null>(null);
}
