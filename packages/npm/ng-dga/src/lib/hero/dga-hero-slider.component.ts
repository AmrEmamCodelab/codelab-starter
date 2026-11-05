import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  DOCUMENT,
  effect,
  ElementRef,
  inject,
  input,
  model,
  signal,
  viewChildren,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { DgaIconComponent } from '../icon/dga-icon.component';
import { isRightToLeft } from '../internal/direction';
import { createDgaId } from '../internal/unique-id';
import {
  clampSlide,
  DGA_HERO_SLIDER_DEFAULT_INTERVAL_MS,
  DGA_REDUCED_MOTION_QUERY,
  type DgaHeroSlide,
  DgaSlideStep,
  effectiveInterval,
  isAutoRotating,
  slideStepForKey,
  stepSlide,
} from './hero-slider-state';

const MEDIA_CHANGE_EVENT = 'change';

/**
 * The DGA hero slider: a carousel of hero slides (WAI-ARIA carousel pattern) for a portal landing page. Slides are
 * data (`slides`, already translated); each one shows a title, an optional subtitle, an optional decorative image and
 * an optional call-to-action router link. Controls: previous and next buttons, one picker dot per slide
 * (`aria-current` on the shown one; ArrowLeft/ArrowRight move along the visual order, mirrored in RTL) and, with
 * autoplay, a pause/play button. Every control label is an input, already translated.
 *
 * Autoplay is off by default. When on, it never runs under `prefers-reduced-motion: reduce`, holds while the pointer
 * or keyboard focus is inside the slider, stops for good when the user presses pause, and never advances faster than
 * {@link DGA_HERO_SLIDER_MIN_INTERVAL_MS}. While it rotates the slide region is `aria-live="off"`; otherwise
 * `polite`, so a manual change is announced.
 *
 * @example
 * <dga-hero-slider [slides]="slides()" [label]="'portal.hero.label' | t"
 *   [previousLabel]="'common.previous' | t" [nextLabel]="'common.next' | t"
 *   [pauseLabel]="'common.pause' | t" [playLabel]="'common.play' | t" autoplay />
 */
@Component({
  selector: 'dga-hero-slider',
  imports: [RouterLink, DgaIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-hero-slider.component.html',
  styleUrl: './dga-hero-slider.component.css',
  host: {
    class: 'dga-hero-slider',
    '(mouseenter)': 'pointerInside.set(true)',
    '(mouseleave)': 'pointerInside.set(false)',
    '(focusin)': 'focusInside.set(true)',
    '(focusout)': 'onFocusout($event)',
  },
})
export class DgaHeroSliderComponent {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly document = inject(DOCUMENT);
  private readonly dots = viewChildren<ElementRef<HTMLButtonElement>>('dot');
  private readonly baseId = createDgaId('dga-hero-slider');

  readonly slides = input.required<readonly DgaHeroSlide[]>();
  /** The shown slide, two-way bindable with [(index)]. */
  readonly index = model(0);
  /** Accessible name of the slider region (already translated). */
  readonly label = input.required<string>();
  readonly previousLabel = input.required<string>();
  readonly nextLabel = input.required<string>();
  /** Label of the rotation button while slides rotate. */
  readonly pauseLabel = input.required<string>();
  /** Label of the rotation button while rotation is paused. */
  readonly playLabel = input.required<string>();
  /** Optional translated aria-roledescription of the region ("carousel") and of each slide ("slide"). */
  readonly roleDescription = input<string | null>(null);
  readonly slideRoleDescription = input<string | null>(null);
  /** Advance on a timer. Off by default; never runs under prefers-reduced-motion. */
  readonly autoplay = input(false, { transform: booleanAttribute });
  readonly intervalMs = input(DGA_HERO_SLIDER_DEFAULT_INTERVAL_MS);

  readonly pausedByUser = signal(false);
  readonly pointerInside = signal(false);
  readonly focusInside = signal(false);
  readonly reducedMotion = signal(false);

  readonly viewportId = `${this.baseId}-slides`;
  readonly current = computed(() => clampSlide(this.index(), this.slides().length));
  readonly rotating = computed(() =>
    isAutoRotating({
      autoplay: this.autoplay(),
      pausedByUser: this.pausedByUser(),
      interacting: this.pointerInside() || this.focusInside(),
      reducedMotion: this.reducedMotion(),
      slideCount: this.slides().length,
    }),
  );

  constructor() {
    this.watchReducedMotion();
    effect((onCleanup) => {
      if (!this.rotating()) return;
      const timer = setInterval(() => this.go(DgaSlideStep.Next), effectiveInterval(this.intervalMs()));
      onCleanup(() => clearInterval(timer));
    });
  }

  slideId(position: number): string {
    return `${this.baseId}-slide-${position}`;
  }

  previous(): void {
    this.go(DgaSlideStep.Previous);
  }

  next(): void {
    this.go(DgaSlideStep.Next);
  }

  show(position: number): void {
    this.index.set(clampSlide(position, this.slides().length));
  }

  togglePause(): void {
    this.pausedByUser.update((paused) => !paused);
  }

  onDotKeydown(event: KeyboardEvent): void {
    const step = slideStepForKey(event.key, isRightToLeft(this.host.nativeElement));
    if (step === null) return;
    event.preventDefault();
    this.go(step);
    this.dots()[this.current()]?.nativeElement.focus();
  }

  onFocusout(event: FocusEvent): void {
    const next = event.relatedTarget as Node | null;
    if (next !== null && this.host.nativeElement.contains(next)) return;
    this.focusInside.set(false);
  }

  private go(step: DgaSlideStep): void {
    this.index.set(stepSlide(this.current(), this.slides().length, step));
  }

  private watchReducedMotion(): void {
    const query = this.document.defaultView?.matchMedia?.(DGA_REDUCED_MOTION_QUERY);
    if (!query) return;
    this.reducedMotion.set(query.matches);
    const listener = (change: MediaQueryListEvent) => this.reducedMotion.set(change.matches);
    query.addEventListener?.(MEDIA_CHANGE_EVENT, listener);
    inject(DestroyRef).onDestroy(() => query.removeEventListener?.(MEDIA_CHANGE_EVENT, listener));
  }
}
