import { booleanAttribute, ChangeDetectionStrategy, Component, computed, input, model } from '@angular/core';
import { DgaIconComponent } from '../icon/dga-icon.component';
import { hasText, joinIds } from '../internal/aria';
import { createDgaId } from '../internal/unique-id';

/**
 * The DGA checkbox: a native checkbox (visually replaced by a token-styled box) with its label.
 *
 * - The state is a signal model: bind it with [(checked)], or with a signal-forms field, which it serves as a
 *   checkbox control (checked, touched, disabled, required, invalid, name).
 * - The label is the `label` input or projected content (already translated); clicking it toggles the box.
 * - `indeterminate` shows a mixed state (`aria-checked="mixed"` through the native property) until the user toggles.
 * - `touched` becomes true on blur.
 *
 * @example
 * <dga-checkbox [(checked)]="rememberMe" [label]="'auth.rememberMe' | translate" />
 */
@Component({
  selector: 'dga-checkbox',
  imports: [DgaIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-checkbox.component.html',
  styleUrl: './dga-checkbox.component.css',
})
export class DgaCheckboxComponent {
  readonly checked = model(false);
  readonly touched = model(false);
  /** Visible label (already translated); projected content is shown after it. */
  readonly label = input<string | null>(null);
  readonly hint = input<string | null>(null);
  /** The error message (already translated); a non-empty error marks the checkbox invalid. */
  readonly error = input<string | null>(null);
  readonly name = input<string | null>(null);
  readonly indeterminate = input(false, { transform: booleanAttribute });
  readonly required = input(false, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly invalid = input(false, { transform: booleanAttribute });

  readonly inputId = createDgaId('dga-checkbox');
  readonly hintId = `${this.inputId}-hint`;
  readonly errorId = `${this.inputId}-error`;

  readonly hasError = computed(() => hasText(this.error()));
  readonly isInvalid = computed(() => this.invalid() || this.hasError());
  readonly describedBy = computed(() =>
    joinIds([this.hasError() ? this.errorId : null, hasText(this.hint()) ? this.hintId : null]),
  );
  /** The box shows a dash while indeterminate, otherwise a tick when checked. */
  readonly mark = computed<'dash' | 'tick' | null>(() => {
    if (this.indeterminate()) return 'dash';
    return this.checked() ? 'tick' : null;
  });

  onChange(event: Event): void {
    this.checked.set((event.target as HTMLInputElement).checked);
  }

  onBlur(): void {
    if (!this.touched()) this.touched.set(true);
  }
}
