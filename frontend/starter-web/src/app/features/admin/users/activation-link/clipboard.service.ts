import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';

/**
 * Copies text to the system clipboard (`navigator.clipboard`, which needs a secure context: https or localhost).
 * Copying is best-effort: a browser without the API, or one that refuses the write, answers false and the screen
 * asks the admin to copy the selected text by hand.
 */
@Injectable({ providedIn: 'root' })
export class ClipboardService {
  private readonly document = inject(DOCUMENT);

  async copy(text: string): Promise<boolean> {
    const clipboard = this.document.defaultView?.navigator.clipboard;
    if (!clipboard) {
      return false;
    }
    try {
      await clipboard.writeText(text);
      return true;
    } catch (error: unknown) {
      if (error instanceof DOMException) {
        return false;
      }
      throw error;
    }
  }
}
