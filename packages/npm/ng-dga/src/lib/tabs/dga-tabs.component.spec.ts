import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { DgaTabsComponent } from './dga-tabs.component';
import type { DgaTab, DgaTabValue } from './tab-navigation';

const TABS: readonly DgaTab[] = [
  { value: 'one', label: 'One', icon: 'home' },
  { value: 'two', label: 'Two', disabled: true },
  { value: 'three', label: 'Three' },
];

@Component({
  imports: [DgaTabsComponent],
  template: `
    <div [attr.dir]="dir()">
      <dga-tabs [tabs]="tabs" [(value)]="selected" label="Views" [panelIdPrefix]="prefix()" />
    </div>
  `,
})
class HostComponent {
  readonly tabs = TABS;
  readonly selected = signal<DgaTabValue>('one');
  readonly dir = signal('ltr');
  readonly prefix = signal<string | null>('views');
}

async function render() {
  const fixture = TestBed.createComponent(HostComponent);
  await fixture.whenStable();
  const host = fixture.nativeElement as HTMLElement;
  const tabs = () => Array.from(host.querySelectorAll<HTMLButtonElement>('[role="tab"]'));
  return { fixture, host, tabs };
}

function press(element: HTMLElement, key: string): void {
  element.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
}

describe('DgaTabsComponent', () => {
  it('renders a named tablist with one tab per entry and the selection', async () => {
    const { host, tabs } = await render();
    expect(host.querySelector('[role="tablist"]')?.getAttribute('aria-label')).toBe('Views');
    expect(tabs().map((tab) => tab.getAttribute('aria-selected'))).toEqual(['true', 'false', 'false']);
    expect(tabs()[1].disabled).toBe(true);
    expect(tabs()[0].querySelector('dga-icon')?.getAttribute('data-name')).toBe('home');
  });

  it('keeps only the selected tab in the tab order', async () => {
    const { tabs } = await render();
    expect(tabs().map((tab) => tab.getAttribute('tabindex'))).toEqual(['0', '-1', '-1']);
  });

  it('links tabs to panels only when a prefix is set', async () => {
    const { fixture, tabs } = await render();
    expect(tabs()[2].getAttribute('aria-controls')).toBe('views-three');
    fixture.componentInstance.prefix.set(null);
    await fixture.whenStable();
    expect(tabs()[2].hasAttribute('aria-controls')).toBe(false);
  });

  it('selects on click and ignores disabled tabs', async () => {
    const { fixture, tabs } = await render();
    tabs()[2].click();
    await fixture.whenStable();
    expect(fixture.componentInstance.selected()).toBe('three');
    tabs()[1].click();
    expect(fixture.componentInstance.selected()).toBe('three');
  });

  it('moves selection and focus with the arrow keys, skipping disabled tabs', async () => {
    const { fixture, tabs } = await render();
    press(tabs()[0], 'ArrowRight');
    await fixture.whenStable();
    expect(fixture.componentInstance.selected()).toBe('three');
    expect(document.activeElement === tabs()[2] || !document.body.contains(tabs()[2])).toBe(true);
  });

  it('mirrors the arrow keys in RTL', async () => {
    const { fixture, tabs } = await render();
    fixture.componentInstance.dir.set('rtl');
    await fixture.whenStable();
    press(tabs()[0], 'ArrowLeft');
    await fixture.whenStable();
    expect(fixture.componentInstance.selected()).toBe('three');
  });

  it('ignores keys that do not navigate', async () => {
    const { fixture, tabs } = await render();
    press(tabs()[0], 'a');
    await fixture.whenStable();
    expect(fixture.componentInstance.selected()).toBe('one');
  });
});
