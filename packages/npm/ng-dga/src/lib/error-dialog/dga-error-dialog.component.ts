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
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { DgaButtonComponent } from '../button/dga-button.component';
import { DgaModalDialog } from '../confirm-dialog/modal-dialog';
import { DgaIconComponent } from '../icon/dga-icon.component';
import type { DgaIconName } from '../icon/dga-icon-names.generated';
import { hasText } from '../internal/aria';
import { createDgaId } from '../internal/unique-id';
import { DGA_CLIPBOARD } from './dga-clipboard';
import {
  copyButtonAccessibleName,
  copyButtonLabel,
  DGA_COPY_FEEDBACK_MS,
  DGA_ERROR_REFERENCE_CODE_TEST_ID,
  DGA_ERROR_REFERENCE_COPY_TEST_ID,
  showsCopyButton,
} from './error-reference';

const NATIVE_BUTTON_SELECTOR = 'button';
const COPY_ICON: DgaIconName = 'file';
const COPIED_ICON: DgaIconName = 'check';

/**
 * The DGA error dialog: a modal report of a failure, with the reference code a user reads out to support. It is
 * presentational: the caller decides what failed and passes translated texts; the dialog never parses an error.
 *
 * - CONTROLLED: `open` shows it as a native modal `<dialog>` (`role="alertdialog"`, named by `title`, described by
 *   `message`). The close button and Escape emit `closed`; the caller sets `open` back to false.
 * - `referenceCode` (language-neutral, shown left-to-right even in Arabic) appears under `referenceLabel`. With a
 *   `copyLabel`, a copy button writes the code to the clipboard (`DGA_CLIPBOARD`), reads `copiedLabel` for a moment
 *   and announces it politely, and emits `copied`. `copyAriaLabel` names the button for assistive technology ("Copy the
 *   reference code") when the visible label alone is too terse.
 * - The code carries `data-testid="error-reference-code"` and the copy button `data-testid="error-reference-copy"`
 *   ({@link DGA_ERROR_REFERENCE_CODE_TEST_ID}, {@link DGA_ERROR_REFERENCE_COPY_TEST_ID}) for browser tests.
 * - Focus moves to the close button on open, Tab stays inside, and focus returns to the opener on close.
 *
 * @example
 * <dga-error-dialog [open]="failed()" [title]="'errors.title' | t" [message]="messageKey() | t"
 *   [referenceLabel]="'errors.reference' | t" [referenceCode]="code()" [copyLabel]="'common.copy' | t"
 *   [copiedLabel]="'common.copied' | t" [copyAriaLabel]="'errors.copy-reference' | t" [closeLabel]="'common.close' | t" (closed)="dismiss()" />
 */
@Component({
  selector: 'dga-error-dialog',
  imports: [DgaButtonComponent, DgaIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-error-dialog.component.html',
  styleUrl: './dga-error-dialog.component.css',
})
export class DgaErrorDialogComponent {
  readonly open = input(false, { transform: booleanAttribute });
  /** The heading (already translated); names the dialog. */
  readonly title = input.required<string>();
  /** What went wrong and what to do (already translated); describes the dialog. */
  readonly message = input.required<string>();
  /** The label above the reference code (already translated). */
  readonly referenceLabel = input<string | null>(null);
  /** The short code support looks the failure up by; the block is hidden without one. */
  readonly referenceCode = input<string | null>(null);
  /** The copy button's label (already translated); without it there is no copy button. */
  readonly copyLabel = input<string | null>(null);
  /** The copy button's label right after a copy, also announced (already translated). */
  readonly copiedLabel = input<string | null>(null);
  /** The copy button's accessible name (already translated); without it the visible label names the button. */
  readonly copyAriaLabel = input<string | null>(null);
  readonly closeLabel = input.required<string>();
  readonly closed = output<void>();
  /** Emitted with the copied code after a successful copy. */
  readonly copied = output<string>();

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly closeButton = viewChild.required<unknown, ElementRef<HTMLElement>>('closeButton', { read: ElementRef });
  private readonly modal = new DgaModalDialog(inject(DOCUMENT));
  private readonly clipboard = inject(DGA_CLIPBOARD);
  private revertTimer: ReturnType<typeof setTimeout> | undefined;

  readonly referenceCodeTestId = DGA_ERROR_REFERENCE_CODE_TEST_ID;
  readonly copyTestId = DGA_ERROR_REFERENCE_COPY_TEST_ID;
  readonly titleId = createDgaId('dga-error-dialog-title');
  readonly messageId = `${this.titleId}-message`;
  /** True for a moment after a successful copy. */
  readonly justCopied = signal(false);
  readonly hasReference = computed(() => hasText(this.referenceCode()));
  private readonly referenceTexts = computed(() => ({
    referenceCode: this.referenceCode(),
    copyLabel: this.copyLabel(),
    copiedLabel: this.copiedLabel(),
    copyAriaLabel: this.copyAriaLabel(),
  }));
  readonly canCopy = computed(() => showsCopyButton(this.referenceTexts()));
  readonly copyText = computed(() => copyButtonLabel(this.referenceTexts(), this.justCopied()));
  readonly copyName = computed(() => copyButtonAccessibleName(this.referenceTexts()));
  readonly copyIcon = computed(() => (this.justCopied() ? COPIED_ICON : COPY_ICON));

  constructor() {
    afterRenderEffect({
      write: () => {
        const open = this.open();
        untracked(() => (open ? this.show() : this.modal.hide(this.dialog().nativeElement)));
      },
    });
    inject(DestroyRef).onDestroy(() => {
      clearTimeout(this.revertTimer);
      this.modal.hide(this.dialog().nativeElement);
    });
  }

  close(): void {
    this.closed.emit();
  }

  async copy(): Promise<void> {
    const code = this.referenceCode();
    if (!hasText(code) || !(await this.clipboard.write(code))) return;
    this.justCopied.set(true);
    this.copied.emit(code);
    clearTimeout(this.revertTimer);
    this.revertTimer = setTimeout(() => this.justCopied.set(false), DGA_COPY_FEEDBACK_MS);
  }

  onKeydown(event: KeyboardEvent): void {
    this.modal.handleKeydown(this.dialog().nativeElement, event, () => this.close());
  }

  onNativeCancel(event: Event): void {
    this.modal.handleNativeCancel(event, () => this.close());
  }

  private show(): void {
    this.modal.show(this.dialog().nativeElement, this.closeButton().nativeElement.querySelector<HTMLElement>(NATIVE_BUTTON_SELECTOR));
  }
}
