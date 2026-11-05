import {
  afterRenderEffect,
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  DOCUMENT,
  ElementRef,
  inject,
  input,
  output,
  untracked,
  viewChild,
} from '@angular/core';
import { DgaButtonComponent } from '../button/dga-button.component';
import { DgaIconComponent } from '../icon/dga-icon.component';
import { hasText } from '../internal/aria';
import { createDgaId } from '../internal/unique-id';
import { confirmDialogIcon, confirmDialogInitialFocus, DgaConfirmDialogFocus, DgaConfirmDialogVariant } from './confirm-dialog-variant';
import { DgaModalDialog } from './modal-dialog';

const NATIVE_BUTTON_SELECTOR = 'button';

/**
 * The DGA confirm dialog: a modal question with a confirm and a cancel button. It only asks; the caller performs the
 * action on `confirmed`.
 *
 * - CONTROLLED: `open` shows it as a native modal `<dialog>` (`role="alertdialog"`, named by `title`, described by
 *   `message`); the caller sets `open` back to false after `confirmed` or `cancelled`.
 * - Cancel, and Escape, emit `cancelled`; Confirm emits `confirmed`. While `busy` nothing emits and Confirm shows a
 *   spinner (keep it open while the action runs).
 * - Focus moves to Confirm on open, or to Cancel for the `danger` variant; Tab stays inside; focus returns to the
 *   opener on close.
 * - Every text arrives translated. Extra body content (a list, a reason field) is projected under the message.
 *
 * @example
 * <dga-confirm-dialog [open]="asking()" variant="danger" [title]="'users.deactivate.title' | t"
 *   [message]="'users.deactivate.message' | t" [confirmLabel]="'common.deactivate' | t" [cancelLabel]="'common.cancel' | t"
 *   (confirmed)="deactivate()" (cancelled)="asking.set(false)" />
 */
@Component({
  selector: 'dga-confirm-dialog',
  imports: [DgaButtonComponent, DgaIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-confirm-dialog.component.html',
  styleUrl: './dga-confirm-dialog.component.css',
})
export class DgaConfirmDialogComponent {
  readonly open = input(false, { transform: booleanAttribute });
  /** The question (already translated); names the dialog. */
  readonly title = input.required<string>();
  /** The explanation under the title (already translated); describes the dialog. */
  readonly message = input<string | null>(null);
  readonly confirmLabel = input.required<string>();
  readonly cancelLabel = input.required<string>();
  readonly variant = input<DgaConfirmDialogVariant>(DgaConfirmDialogVariant.Default);
  /** The action is running: Confirm shows a spinner and neither button nor Escape emits. */
  readonly busy = input(false, { transform: booleanAttribute });
  readonly confirmed = output<void>();
  readonly cancelled = output<void>();

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly confirmButton = viewChild.required<unknown, ElementRef<HTMLElement>>('confirmButton', { read: ElementRef });
  private readonly cancelButton = viewChild.required<unknown, ElementRef<HTMLElement>>('cancelButton', { read: ElementRef });
  private readonly modal = new DgaModalDialog(inject(DOCUMENT));

  readonly titleId = createDgaId('dga-confirm-dialog-title');
  readonly messageId = `${this.titleId}-message`;
  readonly hasMessage = computed(() => hasText(this.message()));
  readonly isDanger = computed(() => this.variant() === DgaConfirmDialogVariant.Danger);
  readonly icon = computed(() => confirmDialogIcon(this.variant()));

  constructor() {
    afterRenderEffect({
      write: () => {
        const open = this.open();
        untracked(() => (open ? this.show() : this.modal.hide(this.dialog().nativeElement)));
      },
    });
    inject(DestroyRef).onDestroy(() => this.modal.hide(this.dialog().nativeElement));
  }

  confirm(): void {
    if (!this.busy()) this.confirmed.emit();
  }

  cancel(): void {
    if (!this.busy()) this.cancelled.emit();
  }

  onKeydown(event: KeyboardEvent): void {
    this.modal.handleKeydown(this.dialog().nativeElement, event, () => this.cancel());
  }

  onNativeCancel(event: Event): void {
    this.modal.handleNativeCancel(event, () => this.cancel());
  }

  private show(): void {
    const focus = confirmDialogInitialFocus(this.variant()) === DgaConfirmDialogFocus.Cancel ? this.cancelButton() : this.confirmButton();
    this.modal.show(this.dialog().nativeElement, focus.nativeElement.querySelector<HTMLElement>(NATIVE_BUTTON_SELECTOR));
  }
}
