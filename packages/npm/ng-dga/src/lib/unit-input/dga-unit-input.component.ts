import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  type ElementRef,
  input,
  model,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { hasText, joinIds } from '../internal/aria';
import { createDgaId } from '../internal/unique-id';
import { optionalNumberAttribute } from '../text-input/dga-text-input.component';
import { DgaNumberIssue, formatNumberText, numberRangeIssue, parseNumberText } from './number-text';

/**
 * The DGA unit input: a number field with its unit (kg, %, km, SAR) shown as a suffix at the inline end of the box,
 * which is the right in English and the left in Arabic. The unit is part of the accessible description, so a screen
 * reader says "72, kg".
 *
 * - The value is a signal model of `number | null`: bind [(value)] or a signal-forms field (value, touched, disabled,
 *   readonly, required, invalid, name). Arabic-Indic digits and the Arabic decimal separator are accepted as typed.
 * - `integer` refuses decimals; `min` and `max` are inclusive.
 * - `issueChange` emits a {@link DgaNumberIssue} ('unreadable', 'below-min', 'above-max') or null when the field's own
 *   problem changes; unreadable text leaves the value null. Map it to a translated `error`.
 * - The suffix sits inside the flex row, so a long unit widens its own slot instead of covering the digits.
 *
 * @example
 * <dga-unit-input [label]="'item.weight' | translate" [unit]="'units.kg' | translate" min="0" [(value)]="weight" />
 */
@Component({
  selector: 'dga-unit-input',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-unit-input.component.html',
  styleUrl: './dga-unit-input.component.css',
})
export class DgaUnitInputComponent {
  readonly value = model<number | null>(null);
  readonly touched = model(false);
  /** The unit shown after the number (already translated), for example 'kg' or 'كجم'. */
  readonly unit = input.required<string>();
  /** Visible label (already translated). */
  readonly label = input<string | null>(null);
  /** Accessible name when there is no visible label (already translated). */
  readonly ariaLabel = input<string | null>(null);
  readonly placeholder = input<string | null>(null);
  readonly hint = input<string | null>(null);
  /** The error message (already translated); a non-empty error marks the input invalid. */
  readonly error = input<string | null>(null);
  readonly name = input<string | null>(null);
  readonly min = input<number | null, unknown>(null, { transform: optionalNumberAttribute });
  readonly max = input<number | null, unknown>(null, { transform: optionalNumberAttribute });
  /** Whole numbers only. */
  readonly integer = input(false, { transform: booleanAttribute });
  readonly required = input(false, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly readonly = input(false, { transform: booleanAttribute });
  /** Marks the input invalid without a message (a form field may set it). */
  readonly invalid = input(false, { transform: booleanAttribute });

  /** The field's own problem, or null; emitted on change only. */
  readonly issueChange = output<DgaNumberIssue | null>();

  readonly inputId = createDgaId('dga-unit-input');
  readonly unitId = `${this.inputId}-unit`;
  readonly hintId = `${this.inputId}-hint`;
  readonly errorId = `${this.inputId}-error`;

  private readonly field = viewChild.required<ElementRef<HTMLInputElement>>('field');
  private readonly unreadable = signal(false);

  readonly inputMode = computed(() => (this.integer() ? 'numeric' : 'decimal'));
  readonly issue = computed<DgaNumberIssue | null>(() =>
    this.unreadable() ? DgaNumberIssue.Unreadable : numberRangeIssue(this.value(), { min: this.min(), max: this.max() }),
  );
  readonly hasError = computed(() => hasText(this.error()));
  readonly isInvalid = computed(() => this.invalid() || this.hasError() || this.issue() !== null);
  readonly describedBy = computed(() =>
    joinIds([this.unitId, this.hasError() ? this.errorId : null, hasText(this.hint()) ? this.hintId : null]),
  );

  private lastIssue: DgaNumberIssue | null = null;

  constructor() {
    // Write the model into the box only when it means another number, so '7.' or Arabic-Indic digits stay as typed.
    effect(() => {
      const element = this.field().nativeElement;
      const value = this.value();
      if (this.readNumber(element.value) === value) return;
      element.value = formatNumberText(value);
      this.unreadable.set(false);
    });
    effect(() => {
      const issue = this.issue();
      if (issue === this.lastIssue) return;
      this.lastIssue = issue;
      this.issueChange.emit(issue);
    });
  }

  onInput(event: Event): void {
    const parsed = parseNumberText((event.target as HTMLInputElement).value, { integer: this.integer() });
    const unreadable = parsed !== undefined && Number.isNaN(parsed);
    this.unreadable.set(unreadable);
    this.value.set(parsed === undefined || unreadable ? null : parsed);
  }

  onBlur(): void {
    if (!this.touched()) this.touched.set(true);
  }

  /** The number the box stands for: null when empty or unreadable. */
  private readNumber(text: string): number | null {
    const parsed = parseNumberText(text, { integer: this.integer() });
    return parsed === undefined || Number.isNaN(parsed) ? null : parsed;
  }
}
