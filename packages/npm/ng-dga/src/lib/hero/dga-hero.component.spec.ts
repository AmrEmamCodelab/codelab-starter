import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { DgaHeroComponent, type DgaHeroVariant } from './dga-hero.component';

@Component({
  imports: [DgaHeroComponent],
  template: `
    <div [attr.dir]="dir()">
      <dga-hero [variant]="variant()" [imageSrc]="image()">
        <h1 dgaHeroTitle>Title</h1>
        <p dgaHeroSubtitle>Subtitle</p>
        <p class="free">Free</p>
        <button dgaHeroActions type="button">Start</button>
      </dga-hero>
    </div>
  `,
})
class HostComponent {
  readonly dir = signal('ltr');
  readonly variant = signal<DgaHeroVariant>('plain');
  readonly image = signal<string | null>(null);
}

async function render(dir: string) {
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentInstance.dir.set(dir);
  await fixture.whenStable();
  const host = fixture.nativeElement as HTMLElement;
  return { fixture, host, hero: host.querySelector<HTMLElement>('dga-hero')! };
}

describe.each(['ltr', 'rtl'])('DgaHeroComponent (%s)', (dir) => {
  it('projects the title, subtitle, free content and actions into their places', async () => {
    const { hero } = await render(dir);
    expect(hero.querySelector('.dga-hero__title h1')?.textContent).toBe('Title');
    expect(hero.querySelector('.dga-hero__subtitle p')?.textContent).toBe('Subtitle');
    expect(hero.querySelector('.dga-hero__content > p.free')).not.toBeNull();
    expect(hero.querySelector('.dga-hero__actions button')?.textContent).toBe('Start');
  });

  it('exposes the variant and shows a decorative image only when one is set', async () => {
    const { fixture, hero } = await render(dir);
    expect(hero.getAttribute('data-variant')).toBe('plain');
    expect(hero.querySelector('img')).toBeNull();
    expect(hero.classList.contains('dga-hero--image')).toBe(false);

    fixture.componentInstance.variant.set('brand');
    fixture.componentInstance.image.set('assets/hero.webp');
    await fixture.whenStable();
    expect(hero.getAttribute('data-variant')).toBe('brand');
    expect(hero.classList.contains('dga-hero--image')).toBe(true);
    const image = hero.querySelector('img');
    expect(image?.getAttribute('src')).toBe('assets/hero.webp');
    expect(image?.getAttribute('alt')).toBe('');
    expect(hero.querySelector('.dga-hero__overlay')?.getAttribute('aria-hidden')).toBe('true');
  });
});
