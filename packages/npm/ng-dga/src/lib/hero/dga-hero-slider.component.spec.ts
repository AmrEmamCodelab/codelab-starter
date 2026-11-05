import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DgaHeroSliderComponent } from './dga-hero-slider.component';
import { DGA_HERO_SLIDER_DEFAULT_INTERVAL_MS, type DgaHeroSlide } from './hero-slider-state';

const SLIDES: readonly DgaHeroSlide[] = [
  { id: 'a', title: 'First', subtitle: 'One', imageSrc: 'assets/a.webp', link: '/services', linkLabel: 'Open' },
  { id: 'b', title: 'Second' },
  { id: 'c', title: 'Third' },
];

@Component({
  imports: [DgaHeroSliderComponent],
  template: `
    <div [attr.dir]="dir()">
      <dga-hero-slider
        [slides]="slides()"
        [(index)]="index"
        label="Highlights"
        previousLabel="Previous"
        nextLabel="Next"
        pauseLabel="Pause"
        playLabel="Play"
        roleDescription="carousel"
        slideRoleDescription="slide"
        [autoplay]="autoplay()"
      />
    </div>
    <button type="button" class="outside">outside</button>
  `,
})
class HostComponent {
  readonly dir = signal('ltr');
  readonly slides = signal<readonly DgaHeroSlide[]>(SLIDES);
  readonly index = signal(0);
  readonly autoplay = signal(false);
}

async function render(dir: string, { autoplay = false } = {}) {
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentInstance.dir.set(dir);
  fixture.componentInstance.autoplay.set(autoplay);
  await fixture.whenStable();
  const host = fixture.nativeElement as HTMLElement;
  const q = <T extends Element>(selector: string) => host.querySelector<T>(selector)!;
  const dots = () => Array.from(host.querySelectorAll<HTMLButtonElement>('.dga-hero-slider__dot'));
  const visibleSlides = () => Array.from(host.querySelectorAll<HTMLElement>('[role="group"]')).filter((slide) => !slide.hidden);
  return { fixture, host, q, dots, visibleSlides, stable: () => fixture.whenStable() };
}

function press(element: Element, key: string): void {
  element.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
}

function stubReducedMotion(matches: boolean): void {
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    value: (query: string) => ({ matches, media: query, addEventListener: vi.fn(), removeEventListener: vi.fn() }),
  });
}

describe.each([
  ['ltr', 'ArrowRight'],
  ['rtl', 'ArrowLeft'],
])('DgaHeroSliderComponent (%s)', (dir, forwardKey) => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([{ path: '**', children: [] }])] });
  });

  afterEach(() => {
    vi.useRealTimers();
    Reflect.deleteProperty(window, 'matchMedia');
  });

  it('is a named region of labelled slides showing only the current one', async () => {
    const { q, visibleSlides } = await render(dir);
    const region = q<HTMLElement>('section');
    expect(region.getAttribute('aria-label')).toBe('Highlights');
    expect(region.getAttribute('aria-roledescription')).toBe('carousel');
    expect(visibleSlides().map((slide) => slide.getAttribute('aria-label'))).toEqual(['First']);
    expect(visibleSlides()[0].getAttribute('aria-roledescription')).toBe('slide');
    expect(visibleSlides()[0].querySelector('h2')?.textContent).toBe('First');
    expect(visibleSlides()[0].querySelector('a')?.getAttribute('href')).toBe('/services');
    expect(visibleSlides()[0].querySelector('img')?.getAttribute('alt')).toBe('');
    expect(q('.dga-hero-slider__slides').getAttribute('aria-live')).toBe('polite');
  });

  it('moves with the labelled previous and next buttons, wrapping around', async () => {
    const { fixture, q, stable } = await render(dir);
    const next = q<HTMLButtonElement>('.dga-hero-slider__next');
    const previous = q<HTMLButtonElement>('.dga-hero-slider__previous');
    expect(next.getAttribute('aria-label')).toBe('Next');
    expect(previous.getAttribute('aria-label')).toBe('Previous');
    expect(next.querySelector('dga-icon')?.getAttribute('data-name')).toBe('chevron-end');
    previous.click();
    await stable();
    expect(fixture.componentInstance.index()).toBe(2);
    next.click();
    next.click();
    await stable();
    expect(fixture.componentInstance.index()).toBe(1);
  });

  it('marks the current dot and jumps to a slide from its dot', async () => {
    const { fixture, dots, stable } = await render(dir);
    expect(dots().map((dot) => dot.getAttribute('aria-current'))).toEqual(['true', null, null]);
    expect(dots()[2].getAttribute('aria-label')).toBe('Third');
    dots()[2].click();
    await stable();
    expect(fixture.componentInstance.index()).toBe(2);
    expect(dots()[2].getAttribute('aria-current')).toBe('true');
  });

  it('moves along the visual order with the arrow keys on the dots and keeps focus on the current dot', async () => {
    const { fixture, dots, stable } = await render(dir);
    dots()[0].focus();
    press(dots()[0], forwardKey);
    await stable();
    expect(fixture.componentInstance.index()).toBe(1);
    expect(document.activeElement).toBe(dots()[1]);
    press(dots()[1], 'Enter');
    await stable();
    expect(fixture.componentInstance.index()).toBe(1);
  });

  it('does not rotate and shows no pause control by default', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    const { fixture, q, stable } = await render(dir);
    vi.advanceTimersByTime(DGA_HERO_SLIDER_DEFAULT_INTERVAL_MS * 2);
    await stable();
    expect(fixture.componentInstance.index()).toBe(0);
    expect(q('.dga-hero-slider__rotation')).toBeNull();
  });

  it('rotates with autoplay, silences the live region, and stops when paused', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    const { fixture, q, stable } = await render(dir, { autoplay: true });
    expect(q('.dga-hero-slider__slides').getAttribute('aria-live')).toBe('off');
    vi.advanceTimersByTime(DGA_HERO_SLIDER_DEFAULT_INTERVAL_MS);
    await stable();
    expect(fixture.componentInstance.index()).toBe(1);

    const rotation = q<HTMLButtonElement>('.dga-hero-slider__rotation');
    expect(rotation.getAttribute('aria-label')).toBe('Pause');
    rotation.click();
    await stable();
    expect(rotation.getAttribute('aria-label')).toBe('Play');
    expect(rotation.querySelector('dga-icon')?.getAttribute('data-name')).toBe('play');
    vi.advanceTimersByTime(DGA_HERO_SLIDER_DEFAULT_INTERVAL_MS * 2);
    await stable();
    expect(fixture.componentInstance.index()).toBe(1);
    expect(q('.dga-hero-slider__slides').getAttribute('aria-live')).toBe('polite');
  });

  it('holds rotation while focus is inside and resumes when it leaves', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    const { fixture, host, q, stable } = await render(dir, { autoplay: true });
    const slider = q<HTMLElement>('dga-hero-slider');
    slider.dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
    await stable();
    vi.advanceTimersByTime(DGA_HERO_SLIDER_DEFAULT_INTERVAL_MS * 2);
    expect(fixture.componentInstance.index()).toBe(0);

    slider.dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: host.querySelector('.outside') }));
    await stable();
    vi.advanceTimersByTime(DGA_HERO_SLIDER_DEFAULT_INTERVAL_MS);
    await stable();
    expect(fixture.componentInstance.index()).toBe(1);
  });

  it('never rotates when the user prefers reduced motion', async () => {
    stubReducedMotion(true);
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    const { fixture, stable } = await render(dir, { autoplay: true });
    vi.advanceTimersByTime(DGA_HERO_SLIDER_DEFAULT_INTERVAL_MS * 2);
    await stable();
    expect(fixture.componentInstance.index()).toBe(0);
  });

  it('shows no controls for a single slide', async () => {
    const { fixture, q, stable } = await render(dir);
    fixture.componentInstance.slides.set([SLIDES[1]]);
    await stable();
    expect(q('.dga-hero-slider__controls')).toBeNull();
  });
});
