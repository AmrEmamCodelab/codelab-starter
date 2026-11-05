import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { DgaCheckboxComponent } from './dga-checkbox.component';

async function render(inputs: Record<string, unknown>) {
  const fixture = TestBed.createComponent(DgaCheckboxComponent);
  for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
  await fixture.whenStable();
  const host = fixture.nativeElement as HTMLElement;
  return { fixture, host, input: host.querySelector('input') as HTMLInputElement };
}

@Component({
  imports: [DgaCheckboxComponent],
  template: `<dga-checkbox [(checked)]="accepted" [(touched)]="touched"><b>Terms</b></dga-checkbox>`,
})
class TwoWayHostComponent {
  readonly accepted = signal(false);
  readonly touched = signal(false);
}

describe('DgaCheckboxComponent', () => {
  it('is a native checkbox wrapped by its label', async () => {
    const { host, input } = await render({ label: 'Remember me' });
    expect(input.type).toBe('checkbox');
    expect(input.closest('label')?.textContent).toContain('Remember me');
    expect(host.querySelector('.dga-checkbox__box')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('reflects checked with a tick and indeterminate with a dash', async () => {
    const checked = await render({ checked: true });
    expect(checked.input.checked).toBe(true);
    expect(checked.fixture.componentInstance.mark()).toBe('tick');
    expect(checked.host.querySelector('.dga-checkbox__mark')?.getAttribute('data-name')).toBe('check');
    const mixed = await render({ indeterminate: '' });
    expect(mixed.input.indeterminate).toBe(true);
    expect(mixed.fixture.componentInstance.mark()).toBe('dash');
    const off = await render({});
    expect(off.fixture.componentInstance.mark()).toBeNull();
  });

  it('describes the input by its error and hint and marks it invalid', async () => {
    const { fixture, input, host } = await render({ error: 'Must accept', hint: 'Read first' });
    const component = fixture.componentInstance;
    expect(input.getAttribute('aria-describedby')).toBe(`${component.errorId} ${component.hintId}`);
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(host.querySelector(`#${component.errorId}`)?.textContent).toBe('Must accept');
  });

  it('passes required, disabled and name to the native input', async () => {
    const { input, host } = await render({ required: true, disabled: true, name: 'terms' });
    expect(input.required).toBe(true);
    expect(input.disabled).toBe(true);
    expect(input.name).toBe('terms');
    expect(host.querySelector('.dga-checkbox__row--disabled')).not.toBeNull();
  });

  it('projects content as the label, toggles both ways and marks touched on blur', async () => {
    const fixture = TestBed.createComponent(TwoWayHostComponent);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    const input = host.querySelector('input') as HTMLInputElement;
    expect(host.querySelector('label b')?.textContent).toBe('Terms');
    input.click();
    input.dispatchEvent(new Event('blur'));
    await fixture.whenStable();
    expect(fixture.componentInstance.accepted()).toBe(true);
    expect(fixture.componentInstance.touched()).toBe(true);
    fixture.componentInstance.accepted.set(false);
    await fixture.whenStable();
    expect(input.checked).toBe(false);
  });
});
