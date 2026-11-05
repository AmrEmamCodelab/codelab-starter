import { InjectionToken } from '@angular/core';

/** Epoch milliseconds now. Injected so specs control time; logic never calls `Date.now()` directly. */
export type Clock = () => number;

export const APP_CLOCK = new InjectionToken<Clock>('APP_CLOCK', {
  providedIn: 'root',
  factory: () => () => Date.now(),
});
