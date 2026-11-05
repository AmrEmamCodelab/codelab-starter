import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  type ElementRef,
  input,
  linkedSignal,
  model,
  output,
  viewChild,
} from '@angular/core';
import { hasText, joinIds } from '../internal/aria';
import { createDgaId } from '../internal/unique-id';
import { dateRangeIssue, type DgaDateIssue, effectiveDateBounds } from '../date-input/iso-date';
import {
  EMPTY_HIJRI_DRAFT,
  type HijriDraft,
  hijriDayChoices,
  hijriDraftIssue,
  hijriIsoFromDraft,
  hijriYearFromText,
  reconcileHijriDraft,
} from './hijri-draft';
import { gregorianIsoToHijriIso, hijriMonthNames } from './umm-al-qura';

/** The default locale of the month names: the starter is Arabic-first. */
const DEFAULT_LOCALE = 'ar';
const MONTH_OFFSET = 1;

/**
 * The DGA Hijri (Umm al-Qura) date input: day and month choosers and a year box in one labelled group, whose value is
 * Hijri ISO text (`yyyy-mm-dd`, or `''` until the parts make a real date).
 *
 * - Conversion runs on the Umm al-Qura month table (the calendar a .NET backend uses); `hijriIsoToGregorianIso` and
 *   `gregorianIsoToHijriIso` convert a value either way. Month names come from the browser's `islamic-umalqura`
 *   calendar in `locale`, so the library ships no text; the day/month/year labels arrive translated.
 * - The value is a signal model: bind [(value)] or a signal-forms field (value, touched, disabled, required, invalid,
 *   name). A half-filled field keeps its parts while the value stays `''`.
 * - `min` and `max` are Hijri ISO dates. `today` is the Gregorian ISO today from the caller's clock (`dgaTodayIso`);
 *   with `notAfterToday` or `notBeforeToday` it narrows the bounds. The component never reads the clock.
 * - `issueChange` emits 'incomplete', 'not-a-date' (a 30th in a 29-day month), 'before-min' or 'after-max' (or null)
 *   when the field's own problem changes. Map it to a translated `error`.
 *
 * @example
 * <dga-hijri-date-input [label]="'profile.birthDateHijri' | translate" [locale]="language()"
 *   [dayLabel]="'common.day' | translate" [monthLabel]="'common.month' | translate" [yearLabel]="'common.year' | translate"
 *   [today]="today()" notAfterToday [error]="birthDateError()" [(value)]="birthDateHijri" />
 */
@Component({
  selector: 'dga-hijri-date-input',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-hijri-date-input.component.html',
  styleUrl: './dga-hijri-date-input.component.css',
})
export class DgaHijriDateInputComponent {
  /** The Hijri date as `yyyy-mm-dd`, or `''`. */
  readonly value = model('');
  readonly touched = model(false);
  /** Visible group label (already translated). */
  readonly label = input<string | null>(null);
  /** Accessible group name when there is no visible label (already translated). */
  readonly ariaLabel = input<string | null>(null);
  /** Names of the three parts (already translated): accessible names and empty-choice texts. */
  readonly dayLabel = input<string | null>(null);
  readonly monthLabel = input<string | null>(null);
  readonly yearLabel = input<string | null>(null);
  readonly hint = input<string | null>(null);
  /** The error message (already translated); a non-empty error marks the field invalid. */
  readonly error = input<string | null>(null);
  readonly name = input<string | null>(null);
  /** The locale of the month names, normally the app language (`ar`, `en`). */
  readonly locale = input(DEFAULT_LOCALE);
  /** The earliest allowed Hijri date, `yyyy-mm-dd`. */
  readonly min = input<string | null>(null);
  /** The latest allowed Hijri date, `yyyy-mm-dd`. */
  readonly max = input<string | null>(null);
  /** Today as a Gregorian `yyyy-mm-dd` from the caller's clock; used only by the today rules. */
  readonly today = input<string | null>(null);
  readonly notAfterToday = input(false, { transform: booleanAttribute });
  readonly notBeforeToday = input(false, { transform: booleanAttribute });
  readonly required = input(false, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });
  /** Marks the field invalid without a message (a form field may set it). */
  readonly invalid = input(false, { transform: booleanAttribute });

  /** The field's own problem, or null; emitted on change only. */
  readonly issueChange = output<DgaDateIssue | null>();

  readonly groupId = createDgaId('dga-hijri-date-input');
  readonly hintId = `${this.groupId}-hint`;
  readonly errorId = `${this.groupId}-error`;
  readonly dayId = `${this.groupId}-day`;
  readonly monthId = `${this.groupId}-month`;
  readonly yearId = `${this.groupId}-year`;

  private readonly yearField = viewChild.required<ElementRef<HTMLInputElement>>('yearField');

  /** The parts on screen: follows the model, but keeps a half-typed entry while the model is ''. */
  readonly draft = linkedSignal<string, HijriDraft>({
    source: this.value,
    computation: (value, previous) => reconcileHijriDraft(value, previous?.value ?? EMPTY_HIJRI_DRAFT),
  });

  readonly dayChoices = computed(() => hijriDayChoices(this.draft()));
  readonly monthNames = computed(() => hijriMonthNames(this.locale()));
  readonly bounds = computed(() => {
    const todayHijri = gregorianIsoToHijriIso(this.today());
    return effectiveDateBounds({
      min: this.min(),
      max: this.max(),
      today: todayHijri === '' ? null : todayHijri,
      notAfterToday: this.notAfterToday(),
      notBeforeToday: this.notBeforeToday(),
    });
  });
  readonly issue = computed(() => hijriDraftIssue(this.draft()) ?? dateRangeIssue(this.value(), this.bounds()));
  readonly hasError = computed(() => hasText(this.error()));
  readonly isInvalid = computed(() => this.invalid() || this.hasError() || this.issue() !== null);
  readonly describedBy = computed(() =>
    joinIds([this.hasError() ? this.errorId : null, hasText(this.hint()) ? this.hintId : null]),
  );

  private lastIssue: DgaDateIssue | null = null;

  constructor() {
    // Write the year into the box only when it means another year, so typing (Arabic-Indic digits included) is kept.
    effect(() => {
      const element = this.yearField().nativeElement;
      const year = this.draft().year;
      if (hijriYearFromText(element.value) === year) return;
      element.value = year === null ? '' : String(year);
    });
    effect(() => {
      const issue = this.issue();
      if (issue === this.lastIssue) return;
      this.lastIssue = issue;
      this.issueChange.emit(issue);
    });
  }

  monthNumber(index: number): number {
    return index + MONTH_OFFSET;
  }

  onDay(event: Event): void {
    this.update({ day: this.selectedNumber(event) });
  }

  onMonth(event: Event): void {
    this.update({ month: this.selectedNumber(event) });
  }

  onYear(event: Event): void {
    this.update({ year: hijriYearFromText((event.target as HTMLInputElement).value) });
  }

  /** Marks the field touched when focus leaves the whole group. */
  onFocusOut(event: FocusEvent): void {
    const next = event.relatedTarget as Node | null;
    const group = event.currentTarget as HTMLElement;
    if (next !== null && group.contains(next)) return;
    if (!this.touched()) this.touched.set(true);
  }

  private selectedNumber(event: Event): number | null {
    const text = (event.target as HTMLSelectElement).value;
    return text === '' ? null : Number(text);
  }

  private update(change: Partial<HijriDraft>): void {
    const next = { ...this.draft(), ...change };
    this.draft.set(next);
    this.value.set(hijriIsoFromDraft(next));
  }
}
