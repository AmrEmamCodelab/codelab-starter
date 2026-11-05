import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { DgaSelectComponent } from './dga-select.component';
import type { DgaSelectOption } from './select-options';

interface City {
  readonly id: number;
}

const CITIES: readonly DgaSelectOption<City>[] = [
  { value: { id: 1 }, label: 'Riyadh' },
  { value: { id: 2 }, label: 'Jeddah', disabled: true },
  { value: { id: 3 }, label: 'Dammam' },
];

@Component({
  imports: [DgaSelectComponent],
  template: `
    <div [attr.dir]="dir()">
      <dga-select
        label="City"
        [options]="options"
        [compareWith]="sameId"
        [placeholder]="placeholder()"
        [required]="required()"
        [(value)]="city"
        [(touched)]="touched"
      />
    </div>
  `,
})
class HostComponent {
  readonly dir = signal('rtl');
  readonly options = CITIES;
  readonly city = signal<City | null>(null);
  readonly touched = signal(false);
  readonly placeholder = signal<string | null>('Choose a city');
  readonly required = signal(false);
  readonly sameId = (a: City, b: City) => a.id === b.id;
}

async function renderHost(setup: (host: HostComponent) => void = () => undefined) {
  const fixture = TestBed.createComponent(HostComponent);
  setup(fixture.componentInstance);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  const select = root.querySelector('select') as HTMLSelectElement;
  return { fixture, root, select, options: () => Array.from(select.options) };
}

async function render(inputs: Record<string, unknown>) {
  const fixture = TestBed.createComponent(DgaSelectComponent<string>);
  fixture.componentRef.setInput('options', [{ value: 'a', label: 'A' }]);
  for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
  await fixture.whenStable();
  const host = fixture.nativeElement as HTMLElement;
  return { fixture, host, select: host.querySelector('select') as HTMLSelectElement };
}

function choose(select: HTMLSelectElement, index: number): void {
  select.selectedIndex = index;
  select.dispatchEvent(new Event('change'));
}

describe('DgaSelectComponent', () => {
  it('renders the placeholder first, then the options with their labels and disabled state', async () => {
    const { options } = await renderHost();
    expect(options().map((option) => option.textContent?.trim())).toEqual(['Choose a city', 'Riyadh', 'Jeddah', 'Dammam']);
    expect(options()[0].selected).toBe(true);
    expect(options()[2].disabled).toBe(true);
  });

  it('shows the option matching the model through compareWith', async () => {
    const { select } = await renderHost((host) => host.city.set({ id: 3 }));
    expect(select.options[select.selectedIndex].textContent?.trim()).toBe('Dammam');
    expect(select.classList).not.toContain('dga-field__select--empty');
  });

  it.each(['rtl', 'ltr'])('binds the value both ways and marks touched on blur in %s', async (dir) => {
    const { fixture, select } = await renderHost((host) => host.dir.set(dir));
    choose(select, 1);
    select.dispatchEvent(new Event('blur'));
    await fixture.whenStable();
    expect(fixture.componentInstance.city()).toEqual({ id: 1 });
    expect(fixture.componentInstance.touched()).toBe(true);
    choose(select, 0);
    await fixture.whenStable();
    expect(fixture.componentInstance.city()).toBeNull();
    fixture.componentInstance.city.set({ id: 3 });
    await fixture.whenStable();
    expect(select.selectedIndex).toBe(3);
  });

  it('keeps an empty choice while nothing is chosen, even without a placeholder', async () => {
    const { fixture, options, select } = await renderHost((host) => host.placeholder.set(null));
    expect(options()).toHaveLength(4);
    expect(options()[0].textContent).toBe('');
    expect(select.selectedIndex).toBe(0);
    fixture.componentInstance.city.set({ id: 1 });
    await fixture.whenStable();
    expect(options().map((option) => option.textContent?.trim())).toEqual(['Riyadh', 'Jeddah', 'Dammam']);
  });

  it('does not offer the placeholder again once a required select has a value', async () => {
    const { fixture, options } = await renderHost((host) => host.required.set(true));
    expect(options()[0].disabled).toBe(false);
    fixture.componentInstance.city.set({ id: 1 });
    await fixture.whenStable();
    expect(options()[0].disabled).toBe(true);
  });

  it('links the visible label, or uses ariaLabel without one', async () => {
    const labelled = await render({ label: 'Region' });
    expect(labelled.host.querySelector('label')?.getAttribute('for')).toBe(labelled.select.id);
    expect(labelled.select.hasAttribute('aria-label')).toBe(false);
    const named = await render({ ariaLabel: 'Region' });
    expect(named.host.querySelector('label')).toBeNull();
    expect(named.select.getAttribute('aria-label')).toBe('Region');
  });

  it('describes the select by its error and hint and marks it invalid on error', async () => {
    const { fixture, host, select } = await render({ hint: 'Pick one', error: 'Required' });
    const component = fixture.componentInstance;
    expect(select.getAttribute('aria-describedby')).toBe(`${component.errorId} ${component.hintId}`);
    expect(select.getAttribute('aria-invalid')).toBe('true');
    expect(host.querySelector(`#${component.errorId}`)?.textContent).toBe('Required');
    const flagged = await render({ invalid: true });
    expect(flagged.select.getAttribute('aria-invalid')).toBe('true');
  });

  it('passes name, required and disabled to the native select and keeps the chevron decorative', async () => {
    const { host, select } = await render({ name: 'region', required: '', disabled: true });
    expect(select.name).toBe('region');
    expect(select.required).toBe(true);
    expect(select.disabled).toBe(true);
    expect(host.querySelector('.dga-field__control--disabled')).not.toBeNull();
    expect(host.querySelector('dga-icon')?.getAttribute('aria-hidden')).toBe('true');
  });
});
