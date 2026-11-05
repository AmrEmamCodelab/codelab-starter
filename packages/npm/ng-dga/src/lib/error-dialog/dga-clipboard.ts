import { DOCUMENT, inject, InjectionToken } from '@angular/core';

/** Writes text to the system clipboard. Resolves true when the text was written, false when the browser refused. */
export interface DgaClipboard {
  write(text: string): Promise<boolean>;
}

/**
 * The clipboard the copy buttons use. The default writes through the async Clipboard API of the page's window and
 * reports false when the API is missing (an insecure origin) or refuses (permission denied). Tests and hosts without a
 * clipboard provide their own.
 */
export const DGA_CLIPBOARD = new InjectionToken<DgaClipboard>('DGA_CLIPBOARD', {
  providedIn: 'root',
  factory: () => browserClipboard(inject(DOCUMENT).defaultView?.navigator.clipboard ?? null),
});

/** A clipboard over the browser's async Clipboard API (or none). */
export function browserClipboard(api: Pick<Clipboard, 'writeText'> | null): DgaClipboard {
  return {
    write: async (text) => {
      if (api === null) return false;
      try {
        await api.writeText(text);
        return true;
      } catch {
        // A refused write (permission denied, document not focused) is an expected outcome: the caller keeps the
        // text visible and selectable and simply shows no "copied" confirmation.
        return false;
      }
    },
  };
}
