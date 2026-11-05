import { hasText } from '../internal/aria';

/** How long the copy button reads "copied" before it offers the copy again, in milliseconds. */
export const DGA_COPY_FEEDBACK_MS = 2000;

/** The texts of the reference block of an error dialog (all already translated, the code language-neutral). */
export interface DgaErrorReferenceTexts {
  readonly referenceCode: string | null;
  readonly copyLabel: string | null;
  readonly copiedLabel: string | null;
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
