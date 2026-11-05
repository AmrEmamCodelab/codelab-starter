import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  type ElementRef,
  afterRenderEffect,
  booleanAttribute,
  computed,
  inject,
  input,
  output,
  untracked,
  viewChild,
} from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { DgaButtonComponent, DgaIconComponent } from '@codelab/ng-dga';
import { apiErrorMessageKey, type ApiError } from '@codelab/ng-security';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { PatternTranslationKeys } from '../pattern-translation-keys';
import { NO_FIELD_ERRORS, allFieldErrors, fieldErrorsFrom, hasFieldErrors, type FieldErrorMap } from './field-errors';
import { focusableWithin, initialFocusTarget, wrapFocusTarget } from './focus-trap';

const ESCAPE_KEY = 'Escape';
const TAB_KEY = 'Tab';
const OPEN_ATTRIBUTE = 'open';

/**
 * The editor dialog page pattern (P4 pattern "admin list + dialog editor", editor half): a modal native `<dialog>`
 * around a form whose fields the screen projects. It is CONTROLLED: `open` shows it, and the screen closes it by
 * setting `open` to false after `cancel`, or after a successful save following `confirm`.
 *
 * - Enter in a field or the confirm button emits `confirm` (the content sits inside a `<form>`); the cancel button,
 *   the close button and Escape emit `cancel`. While `busy`, nothing emits and the confirm button shows a spinner.
 * - Focus moves into the dialog on open (the first `[autofocus]` field, else the first field), Tab and Shift+Tab
 *   cycle inside it, and focus returns to the element that had it before opening.
 * - `error` (the failed save) shows the message and reference code in an alert; `fieldErrors` (default: the error's
 *   own problem+json field errors) lists every field message. Bind each field's `[error]` with the `fieldError` pipe.
 */
@Component({
  selector: 'app-editor-dialog-pattern',
  imports: [DgaButtonComponent, DgaIconComponent, TranslatePipe],
  templateUrl: './editor-dialog-pattern.component.html',
  styleUrl: './editor-dialog-pattern.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EditorDialogPatternComponent {
  readonly open = input(false, { transform: booleanAttribute });
  /** Translation key of the dialog title. */
  readonly titleKey = input.required<string>();
  readonly confirmKey = input<string>(PatternTranslationKeys.Dialog.Confirm);
  readonly cancelKey = input<string>(PatternTranslationKeys.Dialog.Cancel);
  readonly busy = input(false, { transform: booleanAttribute });
  readonly confirmDisabled = input(false, { transform: booleanAttribute });
  /** Styles the confirm button as destructive (deactivate, delete). */
  readonly destructive = input(false, { transform: booleanAttribute });
  /** The failure of the last save, shown above the fields. */
  readonly error = input<ApiError | null>(null);
  /**
   * The codes this screen has `errors.<CODE>` text for; any other code shows the generic message. Left unset, the
   * platform codes of ng-security are used. A module screen binds its own set (platform codes plus module codes).
   */
  readonly translatedCodes = input<ReadonlySet<string> | undefined>(undefined);
  /** Field errors to list; when empty, the `error`'s own problem+json field errors are used. */
  readonly fieldErrors = input<FieldErrorMap>(NO_FIELD_ERRORS);
  readonly confirm = output<void>();
  readonly cancel = output<void>();

  private readonly document = inject(DOCUMENT);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly body = viewChild.required<ElementRef<HTMLElement>>('body');
  private returnFocusTo: HTMLElement | null = null;
  private escapeHandled = false;

  protected readonly keys = PatternTranslationKeys;
  protected readonly titleId = `app-editor-dialog-title-${nextDialogId()}`;
  readonly effectiveFieldErrors = computed(() =>
    hasFieldErrors(this.fieldErrors()) ? this.fieldErrors() : fieldErrorsFrom(this.error()));
  readonly fieldErrorList = computed(() => allFieldErrors(this.effectiveFieldErrors()));
  readonly errorMessageKey = computed(() => {
    const error = this.error();
    return error ? apiErrorMessageKey(error, this.translatedCodes()) : null;
  });
  readonly showSummary = computed(() => this.errorMessageKey() !== null || this.fieldErrorList().length > 0);

  constructor() {
    afterRenderEffect({
      write: () => {
        const open = this.open();
        untracked(() => (open ? this.show() : this.hide()));
      },
    });
    inject(DestroyRef).onDestroy(() => this.hide());
  }

  onSubmit(event: Event): void {
    event.preventDefault();
    if (this.busy() || this.confirmDisabled()) {
      return;
    }
    this.confirm.emit();
  }

  requestCancel(): void {
    if (!this.busy()) {
      this.cancel.emit();
    }
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === ESCAPE_KEY) {
      event.preventDefault();
      this.escapeHandled = true;
      queueMicrotask(() => (this.escapeHandled = false));
      this.requestCancel();
      return;
    }
    if (event.key === TAB_KEY) {
      this.keepFocusInside(event);
    }
  }

  /** The browser's own Escape handling of a modal dialog: never let it close a controlled dialog. */
  onNativeCancel(event: Event): void {
    event.preventDefault();
    if (!this.escapeHandled) {
      this.requestCancel();
    }
  }

  private keepFocusInside(event: KeyboardEvent): void {
    const target = wrapFocusTarget(focusableWithin(this.dialog().nativeElement), this.document.activeElement, event.shiftKey);
    if (target) {
      event.preventDefault();
      target.focus();
    }
  }

  private show(): void {
    const dialog = this.dialog().nativeElement;
    if (dialog.hasAttribute(OPEN_ATTRIBUTE)) {
      return;
    }
    const active = this.document.activeElement;
    this.returnFocusTo = active instanceof HTMLElement ? active : null;
    if (typeof dialog.showModal === 'function') {
      dialog.showModal();
    } else {
      dialog.setAttribute(OPEN_ATTRIBUTE, '');
    }
    initialFocusTarget(dialog, this.body().nativeElement)?.focus();
  }

  private hide(): void {
    const dialog = this.dialog().nativeElement;
    if (!dialog.hasAttribute(OPEN_ATTRIBUTE)) {
      return;
    }
    if (typeof dialog.close === 'function') {
      dialog.close();
    } else {
      dialog.removeAttribute(OPEN_ATTRIBUTE);
    }
    const target = this.returnFocusTo;
    this.returnFocusTo = null;
    if (target?.isConnected) {
      target.focus();
    }
  }
}

let dialogCount = 0;

function nextDialogId(): number {
  dialogCount += 1;
  return dialogCount;
}
