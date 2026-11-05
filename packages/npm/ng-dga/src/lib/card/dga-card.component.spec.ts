import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { DGA_CARD_DEFAULT_HEADING_LEVEL, DgaCardComponent } from './dga-card.component';

@Component({
  imports: [DgaCardComponent],
  template: `
    <dga-card heading="Summary">
      <p class="body">Body</p>
      <div dgaCardActions class="actions">Act</div>
    </dga-card>
  `,
})
class HostComponent {}

async function render(inputs: Record<string, unknown>) {
  const fixture = TestBed.createComponent(DgaCardComponent);
  for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
  await fixture.whenStable();
  const host = fixture.nativeElement as HTMLElement;
  return { fixture, host, section: host.querySelector('section') as HTMLElement };
}

describe('DgaCardComponent', () => {
  it('names the card region by its heading, exposed as a heading of the default level', async () => {
    const { section } = await render({ heading: 'Summary' });
    const heading = section.querySelector('[role="heading"]') as HTMLElement;
    expect(heading.textContent).toBe('Summary');
    expect(heading.getAttribute('aria-level')).toBe(String(DGA_CARD_DEFAULT_HEADING_LEVEL));
    expect(section.getAttribute('aria-labelledby')).toBe(heading.id);
  });

  it('has no heading or label without a heading', async () => {
    const { section } = await render({});
    expect(section.querySelector('[role="heading"]')).toBeNull();
    expect(section.hasAttribute('aria-labelledby')).toBe(false);
  });

  it('clamps the heading level and falls back on NaN', async () => {
    const high = await render({ heading: 'x', headingLevel: 0 });
    expect(high.fixture.componentInstance.ariaLevel()).toBe(1);
    const low = await render({ heading: 'x', headingLevel: '9' });
    expect(low.fixture.componentInstance.ariaLevel()).toBe(6);
    const invalid = await render({ heading: 'x', headingLevel: 'abc' });
    expect(invalid.fixture.componentInstance.ariaLevel()).toBe(DGA_CARD_DEFAULT_HEADING_LEVEL);
  });

  it('renders the description, icon, effect and centring', async () => {
    const { section } = await render({ description: 'More', icon: 'dashboard', effect: 'shadow', centered: true });
    expect(section.querySelector('.dga-card__description')?.textContent).toBe('More');
    expect(section.querySelector('dga-icon')?.getAttribute('data-name')).toBe('dashboard');
    expect(section.getAttribute('data-effect')).toBe('shadow');
    expect(section.classList.contains('dga-card--centered')).toBe(true);
  });

  it('projects the body and the actions slot', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('.dga-card__body .body')?.textContent).toBe('Body');
    expect(host.querySelector('section > .actions')?.textContent).toBe('Act');
  });
});
