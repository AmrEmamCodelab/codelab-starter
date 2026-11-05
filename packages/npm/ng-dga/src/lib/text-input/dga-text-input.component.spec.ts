import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { DgaTextInputComponent, optionalNumberAttribute } from './dga-text-input.component';

async function render(inputs: Record<string, unknown>) {
  const fixture = TestBed.createComponent(DgaTextInputComponent);
  for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
  await fixture.whenStable();
  const host = fixture.nativeElement as HTMLElement;
  return { fixture, host, input: host.querySelector('input') as HTMLInputElement };
}

@Component({
  imports: [DgaTextInputComponent],
  template: `<dga-text-input label="Name" [(value)]="text" [(touched)]="touched" />`,
})
class TwoWayHostComponent {
  readonly text = signal('first');
  readonly touched = signal(false);
}

describe('DgaTextInputComponent', () => {
  it('links the visible label to the input', async () => {
    const { host, input } = await render({ label: 'User name' });
    const label = host.querySelector('label') as HTMLLabelElement;
    expect(label.textContent?.trim()).toBe('User name');
    expect(label.getAttribute('for')).toBe(input.id);
    expect(input.hasAttribute('aria-label')).toBe(false);
  });

  it('uses ariaLabel only when there is no visible label', async () => {
    const { host, input } = await render({ ariaLabel: 'Search' });
    expect(host.querySelector('label')).toBeNull();
    expect(input.getAttribute('aria-label')).toBe('Search');
  });

  it('describes the input by its error and hint and marks it invalid on error', async () => {
    const { fixture, host, input } = await render({ hint: 'Letters only', error: 'Required' });
    const component = fixture.componentInstance;
    expect(input.getAttribute('aria-describedby')).toBe(`${component.errorId} ${component.hintId}`);
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(host.querySelector(`#${component.errorId}`)?.textContent).toBe('Required');
    expect(host.querySelector('.dga-field__control--invalid')).not.toBeNull();
  });

  it('is valid with an empty error and invalid when a form marks it so', async () => {
    const empty = await render({ error: '' });
    expect(empty.input.hasAttribute('aria-invalid')).toBe(false);
    expect(empty.input.hasAttribute('aria-describedby')).toBe(false);
    const flagged = await render({ invalid: true });
    expect(flagged.input.getAttribute('aria-invalid')).toBe('true');
  });

  it('passes the native attributes through', async () => {
    const { input } = await render({
      type: 'password',
      name: 'secret',
      autocomplete: 'current-password',
      placeholder: 'Type here',
      inputMode: 'numeric',
      maxLength: '8',
      required: '',
      readonly: true,
    });
    expect(input.type).toBe('password');
    expect(input.name).toBe('secret');
    expect(input.getAttribute('autocomplete')).toBe('current-password');
    expect(input.placeholder).toBe('Type here');
    expect(input.getAttribute('inputmode')).toBe('numeric');
    expect(input.getAttribute('maxlength')).toBe('8');
    expect(input.required).toBe(true);
    expect(input.readOnly).toBe(true);
  });

  it('shows the required marker hidden from assistive technology', async () => {
    const { host } = await render({ label: 'Name', required: true });
    expect(host.querySelector('.dga-field__required')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('disables the native input', async () => {
    const { input, host } = await render({ disabled: true });
    expect(input.disabled).toBe(true);
    expect(host.querySelector('.dga-field__control--disabled')).not.toBeNull();
  });

  it('renders decorative lead and trail icons', async () => {
    const { host } = await render({ leadIcon: 'search', trailIcon: 'eye' });
    const icons = Array.from(host.querySelectorAll('dga-icon')).map((icon) => icon.getAttribute('data-name'));
    expect(icons).toEqual(['search', 'eye']);
  });

  it('binds the value both ways and marks touched on blur', async () => {
    const fixture = TestBed.createComponent(TwoWayHostComponent);
    await fixture.whenStable();
    const input = (fixture.nativeElement as HTMLElement).querySelector('input') as HTMLInputElement;
    expect(input.value).toBe('first');
    input.value = 'typed';
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('blur'));
    await fixture.whenStable();
    expect(fixture.componentInstance.text()).toBe('typed');
    expect(fixture.componentInstance.touched()).toBe(true);
    fixture.componentInstance.text.set('from model');
    await fixture.whenStable();
    expect(input.value).toBe('from model');
  });
});

describe('optionalNumberAttribute', () => {
  it('parses numbers and treats null, undefined and non-numbers as not set', () => {
    expect(optionalNumberAttribute('12')).toBe(12);
    expect(optionalNumberAttribute(5)).toBe(5);
    expect(optionalNumberAttribute(null)).toBeNull();
    expect(optionalNumberAttribute(undefined)).toBeNull();
    expect(optionalNumberAttribute('abc')).toBeNull();
  });
});
