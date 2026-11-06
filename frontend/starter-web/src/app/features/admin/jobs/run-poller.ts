import { DestroyRef, inject } from '@angular/core';

/** How long a screen waits before it reloads a run that has not finished (milliseconds). */
export const RUN_POLL_INTERVAL_MS = 2000;
/** How many reloads in a row a screen makes while a run stays unfinished (one minute), before it waits for Refresh. */
export const RUN_POLL_MAX_ROUNDS = 30;

/**
 * Reloads a jobs screen while a run is queued or running, so its status changes without a manual refresh. After each
 * load the store calls {@link follow} with whether a run is still active; the poller schedules the next reload, at most
 * {@link RUN_POLL_MAX_ROUNDS} in a row. A load the user asked for calls {@link restart} first. The timer stops when the
 * screen is destroyed. Create it in an injection context (a store's field initializer).
 */
export class RunPoller {
  private timer: ReturnType<typeof setTimeout> | undefined;
  private rounds = 0;

  constructor(private readonly reload: () => void) {
    inject(DestroyRef).onDestroy(() => this.stop());
  }

  /** Schedules the next reload while `active`; an inactive answer ends the series. */
  follow(active: boolean): void {
    this.stop();
    if (!active) {
      this.rounds = 0;
      return;
    }
    if (this.rounds >= RUN_POLL_MAX_ROUNDS) {
      return;
    }
    this.rounds += 1;
    this.timer = setTimeout(() => this.reload(), RUN_POLL_INTERVAL_MS);
  }

  /** A fresh series (the user reloaded or started a run). */
  restart(): void {
    this.stop();
    this.rounds = 0;
  }

  private stop(): void {
    clearTimeout(this.timer);
    this.timer = undefined;
  }
}
