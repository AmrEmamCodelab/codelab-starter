import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { describe, expect, it } from 'vitest';
import { DgaAccordionHeadingLevel, DgaAccordionMode } from './accordion-navigation';
import { DgaAccordionItemComponent } from './dga-accordion-item.component';
import { DgaAccordionComponent } from './dga-accordion.component';

@Component({
  imports: [DgaAccordionComponent, DgaAccordionItemComponent],
  template: `
    <div [attr.dir]="dir()">
      <dga-accordion [mode]="mode()" [headingLevel]="level()">
        <dga-accordion-item label="First" [(expanded)]="first">Body one</dga-accordion-item>
        <dga-accordion-item label="Second" [disabled]="secondDisabled()">Body two</dga-accordion-item>
        <dga-accordion-item label="Third">Body three</dga-accordion-item>
      </dga-accordion>
    </div>
  `,
})
class HostComponent {
  readonly dir = signal('rtl');
  readonly mode = signal<DgaAccordionMode>(DgaAccordionMode.Single);
  readonly level = signal<DgaAccordionHeadingLevel>(DgaAccordionHeadingLevel.H3);
  readonly first = signal(false);
  readonly secondDisabled = signal(false);
}

@Component({
  imports: [DgaAccordionItemComponent],
  template: `<dga-accordion-item label="Alone">Body</dga-accordion-item>`,
})
class StandaloneItemHostComponent {}

async function render(setup: (host: HostComponent) => void = () => undefined) {
  const fixture = TestBed.createComponent(HostComponent);
  setup(fixture.componentInstance);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  const headers = () => Array.from(root.querySelectorAll<HTMLButtonElement>('.dga-accordion-item__header'));
  const panels = () => Array.from(root.querySelectorAll<HTMLElement>('[role="region"]'));
  return { fixture, root, headers, panels };
}

function press(element: HTMLElement, key: string): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
  element.dispatchEvent(event);
  return event;
}

describe('DgaAccordionComponent', () => {
  it('links each header button to its labelled region', async () => {
    const { headers, panels } = await render();
    headers().forEach((header, index) => {
      const panel = panels()[index];
      expect(header.getAttribute('aria-controls')).toBe(panel.id);
      expect(panel.getAttribute('aria-labelledby')).toBe(header.id);
      expect(header.getAttribute('aria-expanded')).toBe('false');
      expect(panel.hidden).toBe(true);
    });
  });

  it('wraps each header in a heading at the configured level', async () => {
    const { fixture, root } = await render();
    const levels = () => Array.from(root.querySelectorAll('[role="heading"]')).map((heading) => heading.getAttribute('aria-level'));
    expect(levels()).toEqual(['3', '3', '3']);
    fixture.componentInstance.level.set(DgaAccordionHeadingLevel.H2);
    await fixture.whenStable();
    expect(levels()).toEqual(['2', '2', '2']);
  });

  it('opens one item at a time in single mode and updates the expanded model', async () => {
    const { fixture, headers, panels } = await render();
    headers()[0].click();
    await fixture.whenStable();
    expect(fixture.componentInstance.first()).toBe(true);
    expect(headers()[0].getAttribute('aria-expanded')).toBe('true');
    expect(panels()[0].hidden).toBe(false);
    headers()[2].click();
    await fixture.whenStable();
    expect(fixture.componentInstance.first()).toBe(false);
    expect(headers().map((header) => header.getAttribute('aria-expanded'))).toEqual(['false', 'false', 'true']);
  });

  it('opens items independently in multiple mode', async () => {
    const { fixture, headers } = await render((host) => host.mode.set(DgaAccordionMode.Multiple));
    headers()[0].click();
    headers()[2].click();
    await fixture.whenStable();
    expect(headers().map((header) => header.getAttribute('aria-expanded'))).toEqual(['true', 'false', 'true']);
    headers()[0].click();
    await fixture.whenStable();
    expect(fixture.componentInstance.first()).toBe(false);
  });

  it('opens an item from the model', async () => {
    const { fixture, headers, panels } = await render();
    fixture.componentInstance.first.set(true);
    await fixture.whenStable();
    expect(headers()[0].getAttribute('aria-expanded')).toBe('true');
    expect(panels()[0].textContent).toContain('Body one');
  });

  it('does not toggle a disabled item', async () => {
    const { fixture, headers } = await render((host) => host.secondDisabled.set(true));
    expect(headers()[1].disabled).toBe(true);
    const items = fixture.debugElement.queryAll(By.directive(DgaAccordionItemComponent));
    (items[1].componentInstance as DgaAccordionItemComponent).toggle();
    await fixture.whenStable();
    expect(headers()[1].getAttribute('aria-expanded')).toBe('false');
  });

  it.each(['rtl', 'ltr'])('moves focus between headers with Up, Down, Home and End in %s', async (dir) => {
    const { headers } = await render((host) => host.dir.set(dir));
    headers()[0].focus();
    const down = press(headers()[0], 'ArrowDown');
    expect(down.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(headers()[1]);
    press(headers()[1], 'End');
    expect(document.activeElement).toBe(headers()[2]);
    press(headers()[2], 'ArrowDown');
    expect(document.activeElement).toBe(headers()[0]);
    press(headers()[0], 'ArrowUp');
    expect(document.activeElement).toBe(headers()[2]);
    press(headers()[2], 'Home');
    expect(document.activeElement).toBe(headers()[0]);
  });

  it('skips a disabled header and leaves other keys alone', async () => {
    const { headers } = await render((host) => host.secondDisabled.set(true));
    headers()[0].focus();
    press(headers()[0], 'ArrowDown');
    expect(document.activeElement).toBe(headers()[2]);
    const other = press(headers()[2], 'ArrowLeft');
    expect(other.defaultPrevented).toBe(false);
    expect(document.activeElement).toBe(headers()[2]);
  });

  it('works as a lone item with heading level 3 and no keyboard movement', async () => {
    const fixture = TestBed.createComponent(StandaloneItemHostComponent);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const header = root.querySelector('button') as HTMLButtonElement;
    expect(root.querySelector('[role="heading"]')?.getAttribute('aria-level')).toBe('3');
    expect(press(header, 'ArrowDown').defaultPrevented).toBe(false);
    header.click();
    await fixture.whenStable();
    expect(header.getAttribute('aria-expanded')).toBe('true');
  });
});
