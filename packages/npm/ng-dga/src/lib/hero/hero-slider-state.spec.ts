import { describe, expect, it } from 'vitest';
import {
  clampSlide,
  DGA_HERO_SLIDER_DEFAULT_INTERVAL_MS,
  DGA_HERO_SLIDER_MIN_INTERVAL_MS,
  DgaSlideKey,
  DgaSlideStep,
  effectiveInterval,
  isAutoRotating,
  slideStepForKey,
  stepSlide,
} from './hero-slider-state';

const ROTATING = { autoplay: true, pausedByUser: false, interacting: false, reducedMotion: false, slideCount: 3 };

describe('hero slider state', () => {
  it('steps forward and backward, wrapping at both ends', () => {
    expect(stepSlide(0, 3, DgaSlideStep.Next)).toBe(1);
    expect(stepSlide(2, 3, DgaSlideStep.Next)).toBe(0);
    expect(stepSlide(0, 3, DgaSlideStep.Previous)).toBe(2);
    expect(stepSlide(0, 0, DgaSlideStep.Next)).toBe(0);
  });

  it('keeps a requested slide inside the slides', () => {
    expect(clampSlide(2, 3)).toBe(2);
    expect(clampSlide(3, 3)).toBe(0);
    expect(clampSlide(-1, 3)).toBe(0);
    expect(clampSlide(1.5, 3)).toBe(0);
  });

  it('maps the arrow keys along the visual order, mirrored in RTL', () => {
    expect(slideStepForKey(DgaSlideKey.ArrowRight, false)).toBe(DgaSlideStep.Next);
    expect(slideStepForKey(DgaSlideKey.ArrowLeft, false)).toBe(DgaSlideStep.Previous);
    expect(slideStepForKey(DgaSlideKey.ArrowLeft, true)).toBe(DgaSlideStep.Next);
    expect(slideStepForKey(DgaSlideKey.ArrowRight, true)).toBe(DgaSlideStep.Previous);
    expect(slideStepForKey('Enter', false)).toBeNull();
  });

  it('rotates only with autoplay, more than one slide, and nothing holding it', () => {
    expect(isAutoRotating(ROTATING)).toBe(true);
    expect(isAutoRotating({ ...ROTATING, autoplay: false })).toBe(false);
    expect(isAutoRotating({ ...ROTATING, pausedByUser: true })).toBe(false);
    expect(isAutoRotating({ ...ROTATING, interacting: true })).toBe(false);
    expect(isAutoRotating({ ...ROTATING, reducedMotion: true })).toBe(false);
    expect(isAutoRotating({ ...ROTATING, slideCount: 1 })).toBe(false);
  });

  it('never rotates faster than the minimum interval', () => {
    expect(effectiveInterval(10_000)).toBe(10_000);
    expect(effectiveInterval(500)).toBe(DGA_HERO_SLIDER_MIN_INTERVAL_MS);
    expect(effectiveInterval(Number.NaN)).toBe(DGA_HERO_SLIDER_DEFAULT_INTERVAL_MS);
  });
});
