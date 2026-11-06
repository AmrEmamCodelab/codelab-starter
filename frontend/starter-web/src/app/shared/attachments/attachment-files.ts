/** Joins extensions into an `accept` attribute value. */
const ACCEPT_SEPARATOR = ',';

const EXTENSION_DOT = '.';

/**
 * The `accept` attribute for a type's allowed extensions (`['.pdf', '.png']` → `.pdf,.png`), so the OS picker offers
 * them first. A hint only: the picker still lets the user choose any file, and the server decides.
 */
export function acceptAttributeFor(allowedExtensions: readonly string[]): string {
  return allowedExtensions.map((extension) => extension.toLowerCase()).join(ACCEPT_SEPARATOR);
}

/**
 * The lowercase extension of `fileName` with its dot (`REPORT.PDF` → `.pdf`), or null when the name has none
 * (no dot, or a dot only in first or last position, such as `.env` or `name.`).
 */
export function extensionOf(fileName: string): string | null {
  const dot = fileName.lastIndexOf(EXTENSION_DOT);
  if (dot <= 0 || dot === fileName.length - 1) return null;
  return fileName.slice(dot).toLowerCase();
}

/**
 * Whether `fileName` ends in one of `allowedExtensions`, case-insensitively. A name without an extension is refused:
 * under an allowlist "no extension" is "not one of the list". For an early hint in the UI only; the attachment service
 * and the API stay authoritative (they also check the file's signature bytes).
 */
export function hasAllowedExtension(fileName: string, allowedExtensions: readonly string[]): boolean {
  const extension = extensionOf(fileName);
  if (extension === null) return false;
  return allowedExtensions.some((allowed) => allowed.toLowerCase() === extension);
}
