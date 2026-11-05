import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { DgaStampStripComponent } from './dga-stamp-strip.component';

@Component({
  imports: [DgaStampStripComponent],
  template: `
    <div [attr.dir]="dir()">
      <dga-stamp-strip text="Official website" verifyLabel="How to verify" [(expanded)]="expanded">
        <p>Details</p>
        <a href="#more">More</a>
      </dga-stamp-strip>
    </div>
  `,
})
class HostComponent {
  readonly dir = signal('ltr');
  readonly expanded = signal(false);
}

async function render(dir: string) {
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentInstance.dir.set(dir);
  await fixture.whenStable();
  const host = fixture.nativeElement as HTMLElement;
  const toggle = host.querySelector<HTMLButtonElement>('.dga-stamp-strip__toggle')!;
  const details = host.querySelector<HTMLElement>('.dga-stamp-strip__details')!;
  return { fixture, host, toggle, details, stable: () => fixture.whenStable() };
}

describe.each(['ltr', 'rtl'])('DgaStampStripComponent (%s)', (dir) => {
  it('states the text and a collapsed disclosure button that controls the details', async () => {
    const { host, toggle, details } = await render(dir);
    expect(host.querySelector('.dga-stamp-strip__text')?.textContent).toBe('Official website');
    expect(toggle.textContent).toContain('How to verify');
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(toggle.getAttribute('aria-controls')).toBe(details.id);
    expect(details.hidden).toBe(true);
    expect(details.querySelector('p')?.textContent).toBe('Details');
    expect(host.querySelector('.dga-stamp-strip__mark')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('toggles the details and two-way binds the state', async () => {
    const { fixture, toggle, details, stable } = await render(dir);
    toggle.click();
    await stable();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(details.hidden).toBe(false);
    expect(fixture.componentInstance.expanded()).toBe(true);

    fixture.componentInstance.expanded.set(false);
    await stable();
    expect(details.hidden).toBe(true);
  });

  it('closes on Escape inside the details and returns focus to the button', async () => {
    const { fixture, toggle, details, stable } = await render(dir);
    toggle.click();
    await stable();
    const link = details.querySelector('a')!;
    link.focus();
    link.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    await stable();
    expect(fixture.componentInstance.expanded()).toBe(false);
    expect(document.activeElement).toBe(toggle);
  });

  it('ignores other keys inside the details', async () => {
    const { fixture, toggle, details, stable } = await render(dir);
    toggle.click();
    await stable();
    details.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));
    await stable();
    expect(fixture.componentInstance.expanded()).toBe(true);
  });
});
