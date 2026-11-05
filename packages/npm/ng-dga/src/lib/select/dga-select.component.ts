import { booleanAttribute, ChangeDetectionStrategy, Component, computed, input, model } from '@angular/core';
import { DgaIconComponent } from '../icon/dga-icon.component';
import { hasText, joinIds } from '../internal/aria';
import { createDgaId } from '../internal/unique-id';
import {
  DGA_SELECT_EMPTY_VALUE,
  type DgaSelectCompareWith,
  type DgaSelectOption,
  dgaSelectDefaultCompare,
  optionForNativeValue,
  optionNativeValue,
  selectedOptionIndex,
} from './select-options';

/**
 * The DGA select: a native <select> styled with the field tokens, for choosing one value from a short list.
 *
 * Native on purpose: the browser gives the full keyboard model (arrows, type-ahead, Home/End, Alt+Down), the phone
 * picker, and screen-reader support in Arabic and English, which a custom listbox would have to rebuild.
 *
 * - The value is a signal model of any type T: bind it with [(value)], or with a signal-forms field, which it serves
 *   as a form value control (value, touched, disabled, required, invalid, name). null means nothing is chosen.
 * - `options` are `{ value, label, disabled? }` with translated labels. `compareWith` matches the model to an option
 *   (default identity); give one when values are objects that are recreated, for example `(a, b) => a.id === b.id`.
 * - `placeholder` (translated) is the empty first choice. While the value is null (or matches no option) an empty
 *   choice is always shown, so the select never pretends the first option is chosen. A required select's placeholder
 *   cannot be chosen again once a value is set.
 * - The label, hint and error are linked (`for`, `aria-describedby`); an error sets `aria-invalid`. `touched`
 *   becomes true on blur.
 *
 * @example
 * <dga-select [label]="'request.region' | translate" [placeholder]="'common.choose' | translate"
 *             [options]="regionOptions()" [compareWith]="sameId" required [(value)]="region" />
 */
@Component({
  selector: 'dga-select',
  imports: [DgaIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-select.component.html',
  styleUrl: './dga-select.component.css',
})
export class DgaSelectComponent<T> {
  readonly value = model<T | null>(null);
  readonly touched = model(false);
  readonly options = input.required<readonly DgaSelectOption<T>[]>();
  readonly compareWith = input<DgaSelectCompareWith<T>>(dgaSelectDefaultCompare);
  /** Visible label (already translated). */
  readonly label = input<string | null>(null);
  /** Accessible name when there is no visible label (already translated). */
  readonly ariaLabel = input<string | null>(null);
  /** The empty first choice (already translated). */
  readonly placeholder = input<string | null>(null);
  readonly hint = input<string | null>(null);
  /** The error message (already translated); a non-empty error marks the select invalid. */
  readonly error = input<string | null>(null);
  readonly name = input<string | null>(null);
  readonly required = input(false, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });
  /** Marks the select invalid without a message (a form field may set it); a non-empty error does so too. */
  readonly invalid = input(false, { transform: booleanAttribute });

  readonly selectId = createDgaId('dga-select');
  readonly hintId = `${this.selectId}-hint`;
  readonly errorId = `${this.selectId}-error`;
  readonly emptyValue = DGA_SELECT_EMPTY_VALUE;

  readonly selectedIndex = computed(() => selectedOptionIndex(this.options(), this.value(), this.compareWith()));
  readonly hasSelection = computed(() => this.selectedIndex() !== -1);
  /** The empty choice shows while nothing is chosen, and stays as a way back when a placeholder is set. */
  readonly showEmptyOption = computed(() => !this.hasSelection() || this.placeholder() !== null);
  /** A required select cannot go back to empty once something is chosen. */
  readonly emptyOptionDisabled = computed(() => this.required() && this.hasSelection());
  readonly hasError = computed(() => hasText(this.error()));
  readonly isInvalid = computed(() => this.invalid() || this.hasError());
  readonly describedBy = computed(() =>
    joinIds([this.hasError() ? this.errorId : null, hasText(this.hint()) ? this.hintId : null]),
  );

  nativeValue(index: number): string {
    return optionNativeValue(index);
  }

  onChange(event: Event): void {
    const option = optionForNativeValue(this.options(), (event.target as HTMLSelectElement).value);
    this.value.set(option === null ? null : option.value);
  }

  onBlur(): void {
    if (!this.touched()) this.touched.set(true);
  }
}
