import { booleanAttribute, ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { DGA_ICON_GLYPHS, DGA_ICONS_MIRRORED_IN_RTL, type DgaIconName } from './dga-icon-names.generated';

/** Icon sizes on the --dga-size-icon-* tokens; `inherit` follows the surrounding font size. */
export type DgaIconSize = 'inherit' | 'xs' | 'sm' | 'md' | 'lg';

/**
 * One glyph of the owned Codelab icon font (ADR-009). Decorative by default (hidden from assistive technology);
 * give it a `label` when the icon alone carries meaning, and it becomes an image with that accessible name.
 * Icons drawn for left-to-right reading (chevrons, arrows, logout) mirror in RTL unless `flipInRtl` is false.
 *
 * Needs the package styles (`@use '@codelab/ng-dga/styles'`), which declare the icon font face.
 *
 * @example
 * <dga-icon name="search" />
 * <dga-icon name="alert" size="lg" [label]="'errors.warning' | translate" />
 */
@Component({
  selector: 'dga-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-icon.component.html',
  styleUrl: './dga-icon.component.css',
  host: {
    class: 'dga-icon',
    '[attr.role]': "label() ? 'img' : null",
    '[attr.aria-label]': 'label()',
    '[attr.aria-hidden]': "label() ? null : 'true'",
    '[attr.data-size]': 'size()',
    '[attr.data-name]': 'name()',
  },
})
export class DgaIconComponent {
  /** The icon to show. */
  readonly name = input.required<DgaIconName>();
  /** Accessible name (already translated). Omit for a decorative icon next to visible text. */
  readonly label = input<string | null>(null);
  readonly size = input<DgaIconSize>('inherit');
  /** Mirror the glyph in RTL layouts. Defaults to the icon's own setting in the icon manifest. */
  readonly flipInRtl = input<boolean | undefined, unknown>(undefined, {
    transform: (value: unknown) => (value === undefined ? undefined : booleanAttribute(value)),
  });

  protected readonly glyph = computed(() => DGA_ICON_GLYPHS[this.name()]);
  readonly mirrored = computed(() => this.flipInRtl() ?? DGA_ICONS_MIRRORED_IN_RTL.has(this.name()));
}
