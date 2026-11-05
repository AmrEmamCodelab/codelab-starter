/**
 * Focus helpers for the modal editor dialog. A native modal `<dialog>` already makes the page behind it inert; these
 * keep Tab cycling inside the dialog (where the browser would otherwise leave for the address bar) and pick the
 * element that receives focus when it opens.
 */

/** Elements that take keyboard focus, in DOM order. */
export const FOCUSABLE_SELECTOR = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled]):not([type="hidden"])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
  '[contenteditable="true"]',
].join(',');

/** The focusable elements inside `root`, skipping hidden ones (`hidden`, `inert` or `aria-hidden` ancestors). */
export function focusableWithin(root: ParentNode): HTMLElement[] {
  return [...root.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR)].filter((element) => !isHidden(element));
}

function isHidden(element: HTMLElement): boolean {
  return element.closest('[hidden], [inert], [aria-hidden="true"]') !== null;
}

/**
 * Where Tab (or Shift+Tab) should go from `current`, or null when the browser's own move stays inside the dialog.
 * From the last element Tab wraps to the first; from the first (or from outside the list) Shift+Tab wraps to the last.
 */
export function wrapFocusTarget(
  focusables: readonly HTMLElement[],
  current: Element | null,
  backwards: boolean,
): HTMLElement | null {
  if (focusables.length === 0) {
    return null;
  }
  const first = focusables[0];
  const last = focusables[focusables.length - 1];
  const index = current ? focusables.indexOf(current as HTMLElement) : -1;
  if (backwards) {
    return index <= 0 ? last : null;
  }
  return index === -1 || current === last ? first : null;
}

/**
 * The element to focus when the dialog opens: the first `[autofocus]` element in the body, else the first focusable
 * element of the body, else the first focusable element of the dialog (its close button).
 */
export function initialFocusTarget(dialog: ParentNode, body: ParentNode | null): HTMLElement | null {
  const inBody = body ? focusableWithin(body) : [];
  return inBody.find((element) => element.hasAttribute('autofocus')) ?? inBody[0] ?? focusableWithin(dialog)[0] ?? null;
}
