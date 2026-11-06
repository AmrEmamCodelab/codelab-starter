import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';

/** The element a transient download link is made of. */
const ANCHOR_TAG = 'a';

/**
 * How long the object URL outlives the click. Revoking it in the same task can cancel the download in some browsers,
 * which start reading the URL only after the click handler returns.
 */
export const OBJECT_URL_RELEASE_DELAY_MS = 1_000;

/**
 * Saves bytes the app already holds as a file: an object URL clicked through a transient `<a download>`, released
 * shortly after. The one owner of "save this download" in the app; the name comes from the server (sanitized there).
 */
@Injectable({ providedIn: 'root' })
export class FileSaver {
  private readonly document = inject(DOCUMENT);

  save(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const anchor = this.document.createElement(ANCHOR_TAG);
    anchor.href = url;
    anchor.download = fileName;
    anchor.rel = 'noopener';
    anchor.click();
    setTimeout(() => URL.revokeObjectURL(url), OBJECT_URL_RELEASE_DELAY_MS);
  }
}
