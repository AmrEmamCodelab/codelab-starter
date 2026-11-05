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
import { dateRangeIssue, DgaDateIssue, effectiveDateBounds } from './iso-date';

/**
 * The DGA Gregorian date input: a native `<input type="date">` with a visible label, hint and error, whose value is
 * plain ISO text (`yyyy-mm-dd`, or `''` when empty), the shape an API sends.
 *
 * Native on purpose: the browser gives the calendar popup, the keyboard model of each date segment, the phone date
 * wheel, and screen-reader support in Arabic and English. The browser shows the date in its own locale order.
 *
 * - The value is a signal model: bind [(value)] or a signal-forms field (value, touched, disabled, readonly, required,
 *   invalid, name). The model only ever holds a real date or `''`.
 * - `min` and `max` are ISO dates. `today` (ISO, from the caller's clock; see `dgaTodayIso`) with `notAfterToday` or
 *   `notBeforeToday` narrows them. The component never reads the clock.
 * - `issueChange` emits a {@link DgaDateIssue} (or null) whenever the field's own problem changes: unreadable text
 *   (the native input holds a half-typed date) or a date outside the bounds. Map it to a translated `error`.
 *
 * @example
 * <dga-date-input [label]="'request.startDate' | translate" [today]="today()" notBeforeToday
 *                 [error]="startDateError()" (issueChange)="startDateIssue.set($event)" [(value)]="startDate" />
 */
@Component({
  selector: 'dga-date-input',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-date-input.component.html',
  styleUrl: './dga-date-input.component.css',
})
export class DgaDateInputComponent {
  /** The date as `yyyy-mm-dd`, or `''` when empty or unreadable. */
  readonly value = model('');
  readonly touched = model(false);
  /** Visible label (already translated). */
  readonly label = input<string | null>(null);
  /** Accessible name when there is no visible label (already translated). */
  readonly ariaLabel = input<string | null>(null);
  readonly hint = input<string | null>(null);
  /** The error message (already translated); a non-empty error marks the input invalid. */
  readonly error = input<string | null>(null);
  readonly name = input<string | null>(null);
  /** The earliest allowed date, `yyyy-mm-dd`. */
  readonly min = input<string | null>(null);
  /** The latest allowed date, `yyyy-mm-dd`. */
  readonly max = input<string | null>(null);
  /** Today as `yyyy-mm-dd` from the caller's clock; used only by the today rules. */
  readonly today = input<string | null>(null);
  readonly notAfterToday = input(false, { transform: booleanAttribute });
  readonly notBeforeToday = input(false, { transform: booleanAttribute });
  readonly required = input(false, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly readonly = input(false, { transform: booleanAttribute });
  /** Marks the input invalid without a message (a form field may set it). */
  readonly invalid = input(false, { transform: booleanAttribute });

  /** The field's own problem, or null; emitted on change only. */
  readonly issueChange = output<DgaDateIssue | null>();

  readonly inputId = createDgaId('dga-date-input');
  readonly hintId = `${this.inputId}-hint`;
  readonly errorId = `${this.inputId}-error`;

  private readonly field = viewChild.required<ElementRef<HTMLInputElement>>('field');
  private readonly unreadable = signal(false);

  readonly bounds = computed(() =>
    effectiveDateBounds({
      min: this.min(),
      max: this.max(),
      today: this.today(),
      notAfterToday: this.notAfterToday(),
      notBeforeToday: this.notBeforeToday(),
    }),
  );
  readonly issue = computed<DgaDateIssue | null>(() =>
    this.unreadable() ? DgaDateIssue.Unreadable : dateRangeIssue(this.value(), this.bounds()),
  );
  readonly hasError = computed(() => hasText(this.error()));
  readonly isInvalid = computed(() => this.invalid() || this.hasError() || this.issue() !== null);
  readonly describedBy = computed(() =>
    joinIds([this.hasError() ? this.errorId : null, hasText(this.hint()) ? this.hintId : null]),
  );

  private lastIssue: DgaDateIssue | null = null;

  constructor() {
    // Write the model into the native input only when they differ, so a half-typed date (native value '') is not
    // wiped by the model's '' echo.
    effect(() => {
      const element = this.field().nativeElement;
      const value = this.value();
      if (element.value === value || (value === '' && element.validity.badInput)) return;
      element.value = value;
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
    const element = event.target as HTMLInputElement;
    this.unreadable.set(element.value === '' && element.validity.badInput);
    this.value.set(element.value);
  }

  onBlur(): void {
    if (!this.touched()) this.touched.set(true);
  }
}
