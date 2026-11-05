/**
 * The modal mechanics shared by <dga-confirm-dialog> and <dga-error-dialog>: a native `<dialog>` opened with
 * `showModal()` (which makes the page behind it inert), Tab and Shift+Tab kept inside it, Escape turned into the
 * dialog's own cancel action, and focus returned to the element that had it before the dialog opened.
 *
 * The dialogs are CONTROLLED: the caller's `open` input shows and hides them. The browser's own Escape handling
 * (`cancel` event) is always prevented, so the dialog never closes behind the caller's back.
 */

/** Elements that take keyboard focus, in DOM order. */
export const DGA_FOCUSABLE_SELECTOR = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled]):not([type="hidden"])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
  '[contenteditable="true"]',
].join(',');

const HIDDEN_ANCESTOR_SELECTOR = '[hidden], [inert], [aria-hidden="true"]';
const OPEN_ATTRIBUTE = 'open';

/** The keys the modal dialogs react to. */
export const DgaDialogKey = {
  Escape: 'Escape',
  Tab: 'Tab',
} as const;
export type DgaDialogKey = (typeof DgaDialogKey)[keyof typeof DgaDialogKey];

/** The focusable elements inside `root`, skipping those under a hidden, inert or aria-hidden ancestor. */
export function focusableWithin(root: ParentNode): HTMLElement[] {
  return [...root.querySelectorAll<HTMLElement>(DGA_FOCUSABLE_SELECTOR)].filter((element) => element.closest(HIDDEN_ANCESTOR_SELECTOR) === null);
}

/** Where a Tab press should move focus, given the dialog's focusable elements and the element that has focus now. */
export interface DgaFocusWrapRequest {
  readonly focusables: readonly HTMLElement[];
  readonly current: Element | null;
  readonly backwards: boolean;
}

/**
 * The element Tab (or Shift+Tab) must move to so focus stays inside the dialog, or null when the browser's own move
 * already stays inside. From the last element Tab wraps to the first; from the first (or from outside the list)
 * Shift+Tab wraps to the last.
 */
export function wrapFocusTarget(request: DgaFocusWrapRequest): HTMLElement | null {
  const { focusables, current, backwards } = request;
  if (focusables.length === 0) return null;
  const first = focusables[0];
  const last = focusables[focusables.length - 1];
  const index = current ? focusables.indexOf(current as HTMLElement) : -1;
  if (backwards) return index <= 0 ? last : null;
  return index === -1 || current === last ? first : null;
}

/**
 * Drives one native modal `<dialog>`: show, hide, the Tab trap and the Escape mapping. One instance per component.
 * Works without `showModal()` (jsdom, very old browsers) by toggling the `open` attribute.
 */
export class DgaModalDialog {
  private returnFocusTo: HTMLElement | null = null;
  private escapeHandled = false;

  constructor(private readonly document: Document) {}

  /** Opens the dialog modally and focuses `initialFocus`; remembers the element to return focus to. */
  show(dialog: HTMLDialogElement, initialFocus: HTMLElement | null): void {
    if (dialog.hasAttribute(OPEN_ATTRIBUTE)) return;
    const active = this.document.activeElement;
    this.returnFocusTo = active instanceof HTMLElement ? active : null;
    if (typeof dialog.showModal === 'function') dialog.showModal();
    else dialog.setAttribute(OPEN_ATTRIBUTE, '');
    (initialFocus ?? focusableWithin(dialog)[0])?.focus();
  }

  /** Closes the dialog and returns focus to the element that had it before `show`, when it is still on the page. */
  hide(dialog: HTMLDialogElement): void {
    if (!dialog.hasAttribute(OPEN_ATTRIBUTE)) return;
    if (typeof dialog.close === 'function') dialog.close();
    else dialog.removeAttribute(OPEN_ATTRIBUTE);
    const target = this.returnFocusTo;
    this.returnFocusTo = null;
    if (target?.isConnected) target.focus();
  }

  /**
   * The dialog's keydown handler: Escape calls `onEscape` (once, even though the browser also fires `cancel`), and
   * Tab keeps focus inside the dialog.
   */
  handleKeydown(dialog: HTMLDialogElement, event: KeyboardEvent, onEscape: () => void): void {
    if (event.key === DgaDialogKey.Escape) {
      event.preventDefault();
      this.escapeHandled = true;
      queueMicrotask(() => (this.escapeHandled = false));
      onEscape();
      return;
    }
    if (event.key !== DgaDialogKey.Tab) return;
    const target = wrapFocusTarget({ focusables: focusableWithin(dialog), current: this.document.activeElement, backwards: event.shiftKey });
    if (target) {
      event.preventDefault();
      target.focus();
    }
  }

  /** The native `cancel` event (the browser's Escape): never let it close a controlled dialog. */
  handleNativeCancel(event: Event, onEscape: () => void): void {
    event.preventDefault();
    if (!this.escapeHandled) onEscape();
  }
}
