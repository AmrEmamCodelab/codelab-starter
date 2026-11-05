import { signal } from '@angular/core';

/**
 * One pending "leave without saving?" question at a time. `ask()` opens it and resolves with the user's answer;
 * a second `ask()` while one is open answers the first with false (stay), so a navigation never hangs.
 */
export class LeaveConfirmation {
  private pending: ((leave: boolean) => void) | null = null;
  private readonly asking = signal(false);

  /** True while the question is shown. */
  readonly open = this.asking.asReadonly();

  ask(): Promise<boolean> {
    this.answer(false);
    this.asking.set(true);
    return new Promise<boolean>((resolve) => {
      this.pending = resolve;
    });
  }

  /** Closes the question with `leave` (true discards the changes and leaves). */
  answer(leave: boolean): void {
    const resolve = this.pending;
    this.pending = null;
    this.asking.set(false);
    resolve?.(leave);
  }
}
