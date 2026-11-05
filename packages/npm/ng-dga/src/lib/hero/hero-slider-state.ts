/** One slide of a <dga-hero-slider>. Text arrives translated; the image is decorative. */
export interface DgaHeroSlide {
  /** Stable key (tracking, ids). */
  readonly id: string;
  readonly title: string;
  readonly subtitle?: string;
  /** A decorative background image served by the app. */
  readonly imageSrc?: string;
  /** Router link of the slide's call to action; shown only with `linkLabel`. */
  readonly link?: string;
  readonly linkLabel?: string;
}

/** Autoplay advances every six seconds unless the app sets another interval. */
export const DGA_HERO_SLIDER_DEFAULT_INTERVAL_MS = 6000;
/** Shorter intervals are raised to this: slides must stay long enough to be read (WCAG 2.2.2). */
export const DGA_HERO_SLIDER_MIN_INTERVAL_MS = 3000;

/** The media query of the user's reduced-motion preference: autoplay never runs under it. */
export const DGA_REDUCED_MOTION_QUERY = '(prefers-reduced-motion: reduce)';

/** The direction a slider moves in. */
export const DgaSlideStep = {
  Previous: -1,
  Next: 1,
} as const;
export type DgaSlideStep = (typeof DgaSlideStep)[keyof typeof DgaSlideStep];

/** The keys the slide picker handles: arrows along the visual order (mirrored in RTL). */
export const DgaSlideKey = {
  ArrowLeft: 'ArrowLeft',
  ArrowRight: 'ArrowRight',
} as const;

/** The step an arrow key on the slide picker makes, or null for any other key. ArrowRight is "next" in LTR only. */
export function slideStepForKey(key: string, rightToLeft: boolean): DgaSlideStep | null {
  const nextKey = rightToLeft ? DgaSlideKey.ArrowLeft : DgaSlideKey.ArrowRight;
  const previousKey = rightToLeft ? DgaSlideKey.ArrowRight : DgaSlideKey.ArrowLeft;
  if (key === nextKey) return DgaSlideStep.Next;
  if (key === previousKey) return DgaSlideStep.Previous;
  return null;
}

/** What decides whether the slider rotates on its own. */
export interface DgaSliderRotation {
  readonly autoplay: boolean;
  /** The user pressed pause. */
  readonly pausedByUser: boolean;
  /** Pointer or keyboard focus is inside the slider (rotation holds while someone reads or operates it). */
  readonly interacting: boolean;
  readonly reducedMotion: boolean;
  readonly slideCount: number;
}

/** The slide after a step from `current`, wrapping around; 0 for an empty slider. */
export function stepSlide(current: number, count: number, step: DgaSlideStep): number {
  if (count === 0) return 0;
  return (((current + step) % count) + count) % count;
}

/** The index to show for a requested one: kept inside the slides (an out-of-range request shows the first slide). */
export function clampSlide(index: number, count: number): number {
  return Number.isInteger(index) && index >= 0 && index < count ? index : 0;
}

/** True when the slider advances by itself right now. */
export function isAutoRotating(state: DgaSliderRotation): boolean {
  return state.autoplay && !state.pausedByUser && !state.interacting && !state.reducedMotion && state.slideCount > 1;
}

/** The autoplay interval to use for a requested one (never below the minimum). */
export function effectiveInterval(requestedMs: number): number {
  return Number.isFinite(requestedMs) ? Math.max(requestedMs, DGA_HERO_SLIDER_MIN_INTERVAL_MS) : DGA_HERO_SLIDER_DEFAULT_INTERVAL_MS;
}
