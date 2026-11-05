import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { DgaInfoFieldComponent, type DgaValueDirection } from './dga-info-field.component';

@Component({
  imports: [DgaInfoFieldComponent],
  template: `
    <dga-info-field [label]="label()" icon="user" [valueDir]="valueDir()" size="lg" [wide]="true" align="start">
      +966 500
    </dga-info-field>
  `,
})
class HostComponent {
  readonly label = input<string | null>('Phone');
  readonly valueDir = input<DgaValueDirection | null>('ltr');
}

async function render(inputs: Record<string, unknown> = {}) {
  const fixture = TestBed.createComponent(HostComponent);
  for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
  await fixture.whenStable();
  const field = (fixture.nativeElement as HTMLElement).querySelector('dga-info-field') as HTMLElement;
  return { field, value: field.querySelector('.dga-info-field__value') as HTMLElement };
}

describe('DgaInfoFieldComponent', () => {
  it('is a group named by its label and projects the value', async () => {
    const { field, value } = await render();
    expect(field.getAttribute('role')).toBe('group');
    const labelId = field.getAttribute('aria-labelledby') as string;
    expect(field.querySelector(`#${labelId}`)?.textContent).toBe('Phone');
    expect(value.textContent?.trim()).toBe('+966 500');
  });

  it('isolates the value in the given direction and sizes it', async () => {
    const { value } = await render();
    expect(value.getAttribute('dir')).toBe('ltr');
    expect(value.getAttribute('data-size')).toBe('lg');
  });

  it('exposes wide and align for the layout styles and shows a decorative icon', async () => {
    const { field } = await render();
    expect(field.classList.contains('dga-info-field--wide')).toBe(true);
    expect(field.getAttribute('data-align')).toBe('start');
    expect(field.querySelector('dga-icon')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('drops the label and the direction when they are not given', async () => {
    const { field, value } = await render({ label: null, valueDir: null });
    expect(field.hasAttribute('aria-labelledby')).toBe(false);
    expect(field.querySelector('.dga-info-field__label')).toBeNull();
    expect(value.hasAttribute('dir')).toBe(false);
  });
});
