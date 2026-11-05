import { booleanAttribute, ChangeDetectionStrategy, Component, computed, input, numberAttribute } from '@angular/core';
import { DgaIconComponent } from '../icon/dga-icon.component';
import type { DgaIconName } from '../icon/dga-icon-names.generated';
import { createDgaId } from '../internal/unique-id';

export type DgaCardEffect = 'stroke' | 'shadow' | 'none';

/** The heading level of a card when none is given; set `headingLevel` to fit the page outline. */
export const DGA_CARD_DEFAULT_HEADING_LEVEL = 3;
const LOWEST_HEADING_LEVEL = 6;
const HIGHEST_HEADING_LEVEL = 1;

/**
 * The DGA card: a surface with an optional featured icon, heading and description, the projected body, and an
 * actions slot (`<div dgaCardActions>...</div>`). The heading is exposed as a heading (`role="heading"`, `aria-level`) and
 * names the card region, so screen-reader users can jump to it.
 *
 * @example
 * <dga-card icon="dashboard" [heading]="'dashboard.summary' | translate" [headingLevel]="2">
 *   <p>{{ 'dashboard.summaryText' | translate }}</p>
 *   <div dgaCardActions><dga-button>{{ 'common.open' | translate }}</dga-button></div>
 * </dga-card>
 */
@Component({
  selector: 'dga-card',
  imports: [DgaIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-card.component.html',
  styleUrl: './dga-card.component.css',
})
export class DgaCardComponent {
  /** The card heading (already translated). Named heading, not title, so no native tooltip appears. */
  readonly heading = input<string | null>(null);
  readonly description = input<string | null>(null);
  readonly icon = input<DgaIconName | null>(null);
  readonly effect = input<DgaCardEffect>('stroke');
  readonly centered = input(false, { transform: booleanAttribute });
  /** The level of the heading (1 to 6) in the page outline; other values are clamped, NaN uses the default. */
  readonly headingLevel = input(DGA_CARD_DEFAULT_HEADING_LEVEL, { transform: numberAttribute });

  readonly ariaLevel = computed(() => {
    const level = Math.trunc(this.headingLevel());
    if (Number.isNaN(level)) return DGA_CARD_DEFAULT_HEADING_LEVEL;
    return Math.min(LOWEST_HEADING_LEVEL, Math.max(HIGHEST_HEADING_LEVEL, level));
  });

  protected readonly headingId = createDgaId('dga-card-heading');
}
