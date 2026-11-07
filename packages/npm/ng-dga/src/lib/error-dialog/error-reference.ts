import { hasText } from '../internal/aria';

/** How long the copy button reads "copied" before it offers the copy again, in milliseconds. */
export const DGA_COPY_FEEDBACK_MS = 2000;

/**
 * The `data-testid` of the reference code element. Browser tests (the Ops Console flow copies the code from here) read
 * it, so it is part of the component's contract: changing it breaks those tests.
 */
export const DGA_ERROR_REFERENCE_CODE_TEST_ID = 'error-reference-code';

/** The `data-testid` of the copy button that writes the reference code to the clipboard. */
export const DGA_ERROR_REFERENCE_COPY_TEST_ID = 'error-reference-copy';

/** The texts of the reference block of an error dialog (all already translated, the code language-neutral). */
export interface DgaErrorReferenceTexts {
  readonly referenceCode: string | null;
  readonly copyLabel: string | null;
  readonly copiedLabel: string | null;
  /** The copy button's accessible name (for example "Copy the reference code"); null keeps the visible label. */
  readonly copyAriaLabel?: string | null;
}

/** The copy button shows only when there is a code to copy and a label to name the button. */
export function showsCopyButton(texts: DgaErrorReferenceTexts): boolean {
  return hasText(texts.referenceCode) && hasText(texts.copyLabel);
}

/** The copy button's label: the "copied" text right after a successful copy (when given), else the copy text. */
export function copyButtonLabel(texts: DgaErrorReferenceTexts, copied: boolean): string {
  if (copied && hasText(texts.copiedLabel)) return texts.copiedLabel;
  return texts.copyLabel ?? '';
}

/**
 * The copy button's accessible name: `copyAriaLabel` when given, otherwise null so the visible label names the button.
 * The name stays put while the button reads "copied": the polite status region announces the copy instead.
 */
export function copyButtonAccessibleName(texts: DgaErrorReferenceTexts): string | null {
  return hasText(texts.copyAriaLabel) ? texts.copyAriaLabel : null;
}
