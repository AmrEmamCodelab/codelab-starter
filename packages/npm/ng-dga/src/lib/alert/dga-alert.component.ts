import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { DgaButtonComponent } from '../button/dga-button.component';
import { DgaIconComponent } from '../icon/dga-icon.component';
import { hasText } from '../internal/aria';
import { createDgaId } from '../internal/unique-id';
import { alertIcon, alertRole, DgaAlertVariant } from './alert-variant';

/**
 * The DGA alert: an inline message box in one of four tones (info, success, warning, error).
 *
 * - `variant` picks the colours and the icon; error and warning use `role="alert"` (announced at once), info and
 *   success `role="status"` (announced politely). Render the alert when the message appears so it is announced.
 * - `title` (optional) is the bold first line; the body is projected content. Both arrive translated.
 * - Dismiss: give `dismissLabel` (the close button's accessible name) to show a close button. Pressing it hides the
 *   alert and emits `dismissed`; to show it again, render a new alert.
 *
 * @example
 * <dga-alert variant="error" [title]="'errors.saveFailed' | translate">{{ 'errors.tryAgain' | translate }}</dga-alert>
 * <dga-alert variant="success" [dismissLabel]="'common.close' | translate" (dismissed)="onClosed()">...</dga-alert>
 */
@Component({
  selector: 'dga-alert',
  imports: [DgaButtonComponent, DgaIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-alert.component.html',
  styleUrl: './dga-alert.component.css',
  // `title` is an input; the null binding keeps a static title="..." from also becoming a tooltip on the host.
  host: { class: 'dga-alert-host', '[attr.title]': 'null' },
})
export class DgaAlertComponent {
  readonly variant = input<DgaAlertVariant>(DgaAlertVariant.Info);
  /** Optional heading line (already translated). */
  readonly title = input<string | null>(null);
  /** Accessible name of the close button (already translated); without it the alert cannot be dismissed. */
  readonly dismissLabel = input<string | null>(null);
  /** Emitted when the user closes the alert. */
  readonly dismissed = output<void>();

  readonly titleId = createDgaId('dga-alert-title');
  readonly visible = signal(true);
  readonly role = computed(() => alertRole(this.variant()));
  readonly icon = computed(() => alertIcon(this.variant()));
  readonly dismissible = computed(() => hasText(this.dismissLabel()));
  readonly hasTitle = computed(() => hasText(this.title()));

  dismiss(): void {
    if (!this.visible()) return;
    this.visible.set(false);
    this.dismissed.emit();
  }
}
