import { booleanAttribute, ChangeDetectionStrategy, Component, computed, input, model, numberAttribute } from '@angular/core';
import { DgaIconComponent } from '../icon/dga-icon.component';
import type { DgaIconName } from '../icon/dga-icon-names.generated';
import { hasText, joinIds } from '../internal/aria';
import { createDgaId } from '../internal/unique-id';

export type DgaTextInputType = 'text' | 'email' | 'password' | 'search' | 'tel' | 'url' | 'number';

/** The native inputmode hint for virtual keyboards. */
export type DgaInputMode = 'text' | 'email' | 'search' | 'tel' | 'url' | 'numeric' | 'decimal' | 'none';

/** Converts an optional number attribute: null, undefined or NaN mean "not set". */
export function optionalNumberAttribute(value: unknown): number | null {
  if (value === null || value === undefined) return null;
  const parsed = numberAttribute(value);
  return Number.isNaN(parsed) ? null : parsed;
}

/**
 * The DGA text input: a native <input> with a visible label, hint, error message and optional decorative icons.
 *
 * - The value is a signal model: bind it with [(value)], or with a signal-forms field, which it serves as a
 *   form value control (value, touched, disabled, readonly, required, invalid, name).
 * - The label, hint and error are linked to the input (`for`, `aria-describedby`); an error sets `aria-invalid`.
 *   Give `label` (visible) or `ariaLabel` (when the surrounding UI already labels it); all texts arrive translated.
 * - `touched` becomes true on blur.
 *
 * @example
 * <dga-text-input [label]="'auth.userName' | translate" autocomplete="username" required [(value)]="userName" />
 * <dga-text-input type="password" [label]="'auth.password' | translate" [error]="passwordError()" [(value)]="password" />
 */
@Component({
  selector: 'dga-text-input',
  imports: [DgaIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-text-input.component.html',
  styleUrl: './dga-text-input.component.css',
})
export class DgaTextInputComponent {
  readonly value = model('');
  readonly touched = model(false);
  /** Visible label (already translated). */
  readonly label = input<string | null>(null);
  /** Accessible name when there is no visible label (already translated). */
  readonly ariaLabel = input<string | null>(null);
  readonly placeholder = input<string | null>(null);
  readonly hint = input<string | null>(null);
  /** The error message (already translated); a non-empty error marks the input invalid. */
  readonly error = input<string | null>(null);
  readonly type = input<DgaTextInputType>('text');
  readonly name = input<string | null>(null);
  readonly autocomplete = input<string | null>(null);
  readonly inputMode = input<DgaInputMode | null>(null);
  readonly maxLength = input<number | null, unknown>(null, { transform: optionalNumberAttribute });
  readonly required = input(false, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly readonly = input(false, { transform: booleanAttribute });
  /** Marks the input invalid without a message (a form field may set it); a non-empty error does so too. */
  readonly invalid = input(false, { transform: booleanAttribute });
  readonly leadIcon = input<DgaIconName | null>(null);
  readonly trailIcon = input<DgaIconName | null>(null);

  readonly inputId = createDgaId('dga-text-input');
  readonly hintId = `${this.inputId}-hint`;
  readonly errorId = `${this.inputId}-error`;

  readonly hasError = computed(() => hasText(this.error()));
  readonly isInvalid = computed(() => this.invalid() || this.hasError());
  readonly describedBy = computed(() =>
    joinIds([this.hasError() ? this.errorId : null, hasText(this.hint()) ? this.hintId : null]),
  );

  onInput(event: Event): void {
    this.value.set((event.target as HTMLInputElement).value);
  }

  onBlur(): void {
    if (!this.touched()) this.touched.set(true);
  }
}
