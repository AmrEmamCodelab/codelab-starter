import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { DgaIconComponent } from './dga-icon.component';
import { DGA_ICON_GLYPHS, DGA_ICONS_MIRRORED_IN_RTL, type DgaIconName } from './dga-icon-names.generated';

async function render(inputs: Record<string, unknown>) {
  const fixture = TestBed.createComponent(DgaIconComponent);
  for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
  await fixture.whenStable();
  const host = fixture.nativeElement as HTMLElement;
  return { fixture, host, glyph: host.querySelector('.dga-icon__glyph') as HTMLElement };
}

describe('DgaIconComponent', () => {
  it('renders the private-use glyph of the named icon', async () => {
    const { glyph } = await render({ name: 'search' });
    expect(glyph.textContent).toBe(DGA_ICON_GLYPHS.search);
    expect(glyph.textContent?.codePointAt(0)).toBeGreaterThanOrEqual(0xe000);
  });

  it('is decorative without a label', async () => {
    const { host } = await render({ name: 'home' });
    expect(host.getAttribute('aria-hidden')).toBe('true');
    expect(host.hasAttribute('role')).toBe(false);
    expect(host.hasAttribute('aria-label')).toBe(false);
  });

  it('becomes an image with an accessible name when labelled', async () => {
    const { host } = await render({ name: 'alert', label: 'Warning' });
    expect(host.getAttribute('role')).toBe('img');
    expect(host.getAttribute('aria-label')).toBe('Warning');
    expect(host.hasAttribute('aria-hidden')).toBe(false);
  });

  it('exposes the size for the token-based styles', async () => {
    const { host } = await render({ name: 'user', size: 'lg' });
    expect(host.getAttribute('data-size')).toBe('lg');
  });

  it('mirrors reading-direction icons by default and keeps the others', async () => {
    const forward = await render({ name: 'chevron-end' });
    const neutral = await render({ name: 'search' });
    expect(forward.glyph.classList.contains('dga-icon__glyph--mirror')).toBe(true);
    expect(neutral.glyph.classList.contains('dga-icon__glyph--mirror')).toBe(false);
  });

  it('lets flipInRtl override the manifest in both directions', async () => {
    const pinned = await render({ name: 'arrow-end', flipInRtl: false });
    const forced = await render({ name: 'search', flipInRtl: '' });
    expect(pinned.fixture.componentInstance.mirrored()).toBe(false);
    expect(forced.fixture.componentInstance.mirrored()).toBe(true);
  });

  it('knows a glyph for every icon name and mirrors only known icons', () => {
    const names = Object.keys(DGA_ICON_GLYPHS) as DgaIconName[];
    expect(new Set(Object.values(DGA_ICON_GLYPHS)).size).toBe(names.length);
    for (const name of DGA_ICONS_MIRRORED_IN_RTL) expect(names).toContain(name);
  });
});
