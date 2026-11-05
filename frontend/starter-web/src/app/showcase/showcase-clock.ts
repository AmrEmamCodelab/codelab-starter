import type { Provider } from '@angular/core';
import { DGA_KSA_TIME_ZONE, dgaTodayIso } from '@codelab/ng-dga';
import { APP_CLOCK, type Clock } from '../core/session/clock';

/**
 * The instant the showcase pretends it is. Every showcase page runs on this pinned clock (route provider), so due
 * dates, "today" limits, relative fixtures and the footer year render the same on every run and the visual baselines
 * need no masks for them.
 */
export const SHOWCASE_NOW_ISO = '2026-11-01T09:00:00+03:00';

/** {@link SHOWCASE_NOW_ISO} as epoch milliseconds. */
export const SHOWCASE_NOW_MS = Date.parse(SHOWCASE_NOW_ISO);

/** The pinned showcase clock. */
export const showcaseClock: Clock = () => SHOWCASE_NOW_MS;

/** Provides the pinned clock to everything under a showcase route (layouts read `APP_CLOCK` too). */
export function provideShowcaseClock(): Provider {
  return { provide: APP_CLOCK, useValue: showcaseClock };
}

/** The Gregorian ISO date (`yyyy-mm-dd`) the clock reads in Saudi time: the `today` input of the date pickers. */
export function showcaseToday(clock: Clock): string {
  return dgaTodayIso({ instant: new Date(clock()), timeZone: DGA_KSA_TIME_ZONE });
}

const MILLISECONDS_PER_HOUR = 3_600_000;

/** An ISO instant `hours` from the clock's now (negative for the past): relative fixtures such as due dates. */
export function hoursFromNow(clock: Clock, hours: number): string {
  return new Date(clock() + hours * MILLISECONDS_PER_HOUR).toISOString();
}

/** A long Gregorian date in the app language and Saudi time ("1 November 2026" in English). */
export function formatShowcaseDate(instantMs: number, language: string): string {
  return new Intl.DateTimeFormat(language, {
    dateStyle: 'long',
    calendar: 'gregory',
    timeZone: DGA_KSA_TIME_ZONE,
  }).format(instantMs);
}
