import { booleanAttribute, ChangeDetectionStrategy, Component, input } from '@angular/core';
import { DgaIconComponent } from '../icon/dga-icon.component';
import type { DgaIconName } from '../icon/dga-icon-names.generated';
import { createDgaId } from '../internal/unique-id';

export type DgaInfoFieldSize = 'md' | 'lg';
export type DgaInfoFieldAlign = 'center' | 'start';
/** The writing direction of the value; `ltr` keeps phone numbers and codes readable inside RTL text. */
export type DgaValueDirection = 'ltr' | 'rtl' | 'auto';

/**
 * The DGA info field: one labelled read-only value (a detail page datum) with an optional decorative leading icon in
 * a circle. The value is projected so the caller keeps its formatting and fallback; the label is already translated.
 * The field is a `role="group"` named by its label, so the label is read with the value.
 *
 * - `wide` spans the full row of a CSS grid; `align="start"` suits multi-line values.
 * - `valueDir="ltr"` isolates a left-to-right value (a phone number, an id) inside RTL text.
 *
 * @example
 * <dga-info-field icon="user" [label]="'profile.userName' | translate">{{ user().userName }}</dga-info-field>
 * <dga-info-field icon="info" [label]="'profile.phone' | translate" valueDir="ltr">{{ user().phone }}</dga-info-field>
 */
@Component({
  selector: 'dga-info-field',
  imports: [DgaIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-info-field.component.html',
  styleUrl: './dga-info-field.component.css',
  host: {
    class: 'dga-info-field',
    role: 'group',
    '[attr.aria-labelledby]': 'label() ? labelId : null',
    '[class.dga-info-field--wide]': 'wide()',
    '[attr.data-align]': 'align()',
  },
})
export class DgaInfoFieldComponent {
  /** The caption above the value (already translated). */
  readonly label = input<string | null>(null);
  readonly icon = input<DgaIconName | null>(null);
  readonly valueDir = input<DgaValueDirection | null>(null);
  readonly size = input<DgaInfoFieldSize>('md');
  readonly wide = input(false, { transform: booleanAttribute });
  readonly align = input<DgaInfoFieldAlign>('center');

  readonly labelId = createDgaId('dga-info-field-label');
}
