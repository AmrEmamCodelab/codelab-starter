import {
  afterRenderEffect,
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  input,
  model,
  numberAttribute,
  viewChild,
} from '@angular/core';
import { hasText, joinIds } from '../internal/aria';
import { createDgaId } from '../internal/unique-id';
import { optionalNumberAttribute } from '../text-input/dga-text-input.component';
import { DGA_TEXTAREA_DEFAULT_COUNTER_TEMPLATE, DGA_TEXTAREA_DEFAULT_ROWS, formatTextareaCounter } from './textarea-counter';

/** The inline block size that lets the textarea shrink before it is measured for auto-resize. */
const AUTO_BLOCK_SIZE = 'auto';
const PIXEL_UNIT = 'px';

/**
 * The DGA textarea: a native <textarea> with a visible label, hint, error message and an optional live counter.
 *
 * - The value is a signal model: bind it with [(value)], or with a signal-forms field, which it serves as a form value
 *   control (value, touched, disabled, readonly, required, invalid, name). `touched` becomes true on blur.
 * - The label, hint, counter and error are linked to the textarea (`for`, `aria-describedby`); an error sets
 *   `aria-invalid`. Give `label` (visible) or `ariaLabel`; all texts arrive translated.
 * - `maxLength` limits the input and shows a counter. `counterLabel` is its translated template with the
 *   placeholders `{count}`, `{max}` and `{remaining}`; the default shows digits only (`12 / 500`).
 * - `autoResize` grows the textarea with its content, starting from `rows`.
 *
 * @example
 * <dga-textarea [label]="'request.notes' | translate" maxLength="500" [counterLabel]="'common.charCounter' | translate"
 *               autoResize [(value)]="notes" />
 */
@Component({
  selector: 'dga-textarea',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-textarea.component.html',
  styleUrl: './dga-textarea.component.css',
})
export class DgaTextareaComponent {
  private readonly textarea = viewChild.required<ElementRef<HTMLTextAreaElement>>('textarea');

  readonly value = model('');
  readonly touched = model(false);
  /** Visible label (already translated). */
  readonly label = input<string | null>(null);
  /** Accessible name when there is no visible label (already translated). */
  readonly ariaLabel = input<string | null>(null);
  readonly placeholder = input<string | null>(null);
  readonly hint = input<string | null>(null);
  /** The error message (already translated); a non-empty error marks the textarea invalid. */
  readonly error = input<string | null>(null);
  readonly name = input<string | null>(null);
  /** Visible text lines (the starting height when `autoResize` is on). */
  readonly rows = input(DGA_TEXTAREA_DEFAULT_ROWS, { transform: (value: unknown) => numberAttribute(value, DGA_TEXTAREA_DEFAULT_ROWS) });
  /** The maximum number of characters; also shows the counter. */
  readonly maxLength = input<number | null, unknown>(null, { transform: optionalNumberAttribute });
  /** The counter template (already translated) with `{count}`, `{max}` and `{remaining}`. */
  readonly counterLabel = input<string | null>(null);
  readonly autoResize = input(false, { transform: booleanAttribute });
  readonly required = input(false, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly readonly = input(false, { transform: booleanAttribute });
  /** Marks the textarea invalid without a message (a form field may set it); a non-empty error does so too. */
  readonly invalid = input(false, { transform: booleanAttribute });

  readonly textareaId = createDgaId('dga-textarea');
  readonly hintId = `${this.textareaId}-hint`;
  readonly errorId = `${this.textareaId}-error`;
  readonly counterId = `${this.textareaId}-counter`;

  readonly hasError = computed(() => hasText(this.error()));
  readonly isInvalid = computed(() => this.invalid() || this.hasError());
  readonly counterText = computed(() => {
    const max = this.maxLength();
    if (max === null) return null;
    const template = hasText(this.counterLabel()) ? (this.counterLabel() as string) : DGA_TEXTAREA_DEFAULT_COUNTER_TEMPLATE;
    return formatTextareaCounter(template, { count: this.value().length, max });
  });
  readonly describedBy = computed(() =>
    joinIds([
      this.hasError() ? this.errorId : null,
      hasText(this.hint()) ? this.hintId : null,
      this.counterText() === null ? null : this.counterId,
    ]),
  );

  constructor() {
    afterRenderEffect({
      write: () => {
        this.value();
        if (this.autoResize()) this.fitToContent();
      },
    });
  }

  onInput(event: Event): void {
    this.value.set((event.target as HTMLTextAreaElement).value);
  }

  onBlur(): void {
    if (!this.touched()) this.touched.set(true);
  }

  private fitToContent(): void {
    const element = this.textarea().nativeElement;
    element.style.blockSize = AUTO_BLOCK_SIZE;
    element.style.blockSize = `${element.scrollHeight}${PIXEL_UNIT}`;
  }
}
