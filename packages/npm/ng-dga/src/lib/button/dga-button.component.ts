import { booleanAttribute, ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { DgaIconComponent } from '../icon/dga-icon.component';
import type { DgaIconName } from '../icon/dga-icon-names.generated';

export type DgaButtonVariant = 'primary' | 'neutral' | 'secondary-solid' | 'secondary-outline' | 'subtle' | 'transparent';
export type DgaButtonSize = 'sm' | 'md' | 'lg';
export type DgaButtonType = 'button' | 'submit' | 'reset';

/**
 * The DGA button: a native <button> with the variant, size, destructive, on-colour, selected, loading and icon
 * matrix of the DGA design system. The label is projected content (already translated by the caller).
 *
 * - `iconOnly` hides the projected label and requires `label` as the accessible name.
 * - `pressed` makes it a toggle button (`aria-pressed`); leave it null for an ordinary button.
 * - `loading` disables the button and marks it busy; the spinner is decorative.
 *
 * Listen with (click) on <dga-button>: a disabled or loading button emits no click.
 *
 * @example
 * <dga-button variant="primary" type="submit" [loading]="saving()">{{ 'common.save' | translate }}</dga-button>
 * <dga-button variant="subtle" iconOnly leadIcon="close" [label]="'common.close' | translate" />
 */
@Component({
  selector: 'dga-button',
  imports: [DgaIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-button.component.html',
  styleUrl: './dga-button.component.css',
  host: {
    class: 'dga-button',
    '[class.dga-button--full]': 'fullWidth()',
  },
})
export class DgaButtonComponent {
  readonly variant = input<DgaButtonVariant>('primary');
  readonly size = input<DgaButtonSize>('lg');
  readonly type = input<DgaButtonType>('button');
  readonly destructive = input(false, { transform: booleanAttribute });
  /** Placed over a dark or brand-coloured surface. */
  readonly onColor = input(false, { transform: booleanAttribute });
  /** Visual selected state (for example the active item of a button group). */
  readonly selected = input(false, { transform: booleanAttribute });
  /** Toggle state: true or false renders aria-pressed; null (the default) is an ordinary button. */
  readonly pressed = input<boolean | null>(null);
  readonly loading = input(false, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly iconOnly = input(false, { transform: booleanAttribute });
  readonly fullWidth = input(false, { transform: booleanAttribute });
  readonly leadIcon = input<DgaIconName | null>(null);
  readonly trailIcon = input<DgaIconName | null>(null);
  /** Accessible name; required with iconOnly, otherwise it overrides the projected label for assistive technology. */
  readonly label = input<string | null>(null);

  /** The native button is disabled while loading as well. */
  readonly isDisabled = computed(() => this.disabled() || this.loading());
}
