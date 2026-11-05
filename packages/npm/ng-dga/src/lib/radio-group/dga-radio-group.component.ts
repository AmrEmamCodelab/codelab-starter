import { booleanAttribute, ChangeDetectionStrategy, Component, computed, input, model } from '@angular/core';
import { hasText, joinIds } from '../internal/aria';
import { createDgaId } from '../internal/unique-id';

export type DgaRadioValue = string | number;

/** One option of a <dga-radio-group>. `label` and `hint` are already translated by the caller. */
export interface DgaRadioOption {
  readonly value: DgaRadioValue;
  readonly label: string;
  readonly hint?: string;
  readonly disabled?: boolean;
}

export type DgaRadioOrientation = 'vertical' | 'horizontal';

/**
 * The DGA radio group: native radio buttons sharing one name inside a `role="radiogroup"`, so the browser gives the
 * standard keyboard behaviour (Tab into the group, arrow keys move and select, following the reading direction).
 *
 * - The selected value is a signal model: bind it with [(value)], or with a signal-forms field, which it serves as a
 *   form value control (value, touched, disabled, required, invalid, name). null means nothing is selected.
 * - The group is named by its visible `label` or by `ariaLabel`; hint and error are linked with aria-describedby.
 * - `touched` becomes true when focus leaves an option.
 *
 * @example
 * <dga-radio-group [label]="'settings.language' | translate" [options]="languages()" [(value)]="language" />
 */
@Component({
  selector: 'dga-radio-group',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-radio-group.component.html',
  styleUrl: './dga-radio-group.component.css',
})
export class DgaRadioGroupComponent {
  readonly value = model<DgaRadioValue | null>(null);
  readonly touched = model(false);
  readonly options = input.required<readonly DgaRadioOption[]>();
  /** Visible group label (already translated). */
  readonly label = input<string | null>(null);
  /** Accessible name when there is no visible label (already translated). */
  readonly ariaLabel = input<string | null>(null);
  readonly hint = input<string | null>(null);
  /** The error message (already translated); a non-empty error marks the group invalid. */
  readonly error = input<string | null>(null);
  /** The native radio name; a unique one is generated when omitted. */
  readonly name = input<string | null>(null);
  readonly orientation = input<DgaRadioOrientation>('vertical');
  readonly required = input(false, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly invalid = input(false, { transform: booleanAttribute });

  readonly groupId = createDgaId('dga-radio-group');
  readonly labelId = `${this.groupId}-label`;
  readonly hintId = `${this.groupId}-hint`;
  readonly errorId = `${this.groupId}-error`;

  readonly radioName = computed(() => this.name() ?? this.groupId);
  readonly hasError = computed(() => hasText(this.error()));
  readonly isInvalid = computed(() => this.invalid() || this.hasError());
  readonly describedBy = computed(() =>
    joinIds([this.hasError() ? this.errorId : null, hasText(this.hint()) ? this.hintId : null]),
  );

  optionId(index: number): string {
    return `${this.groupId}-option-${index}`;
  }

  isOptionDisabled(option: DgaRadioOption): boolean {
    return this.disabled() || option.disabled === true;
  }

  select(option: DgaRadioOption): void {
    if (this.isOptionDisabled(option) || option.value === this.value()) return;
    this.value.set(option.value);
  }

  onBlur(): void {
    if (!this.touched()) this.touched.set(true);
  }
}
