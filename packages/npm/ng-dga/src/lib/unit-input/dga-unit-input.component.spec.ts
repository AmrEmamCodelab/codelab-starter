import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { DgaUnitInputComponent } from './dga-unit-input.component';
import { DgaNumberIssue } from './number-text';

@Component({
  imports: [DgaUnitInputComponent],
  template: `
    <div [attr.dir]="dir()">
      <dga-unit-input label="Weight" unit="kg" min="1" max="300" [(value)]="weight" [(touched)]="touched"
                      (issueChange)="issues.push($event)" />
    </div>
  `,
})
class HostComponent {
  readonly dir = signal('rtl');
  readonly weight = signal<number | null>(72);
  readonly touched = signal(false);
  readonly issues: (DgaNumberIssue | null)[] = [];
}

async function renderHost() {
  const fixture = TestBed.createComponent(HostComponent);
  await fixture.whenStable();
  const element = fixture.nativeElement as HTMLElement;
  return { fixture, host: fixture.componentInstance, element, input: element.querySelector('input') as HTMLInputElement };
}

function type(input: HTMLInputElement, text: string): void {
  input.value = text;
  input.dispatchEvent(new Event('input'));
}

describe('DgaUnitInputComponent', () => {
  it('shows the unit after the input and reads it as part of the description', async () => {
    const { element, input } = await renderHost();
    const unit = element.querySelector('.dga-unit-input__unit') as HTMLElement;
    expect(unit.textContent).toBe('kg');
    expect(input.nextElementSibling).toBe(unit);
    expect(input.getAttribute('aria-describedby')?.split(' ')[0]).toBe(unit.id);
    expect(input.getAttribute('inputmode')).toBe('decimal');
  });

  it('keeps the unit last in the row in RTL and LTR (logical placement)', async () => {
    const { fixture, host, element } = await renderHost();
    const last = () => element.querySelector('.dga-field__control')?.lastElementChild?.className;
    expect(last()).toBe('dga-unit-input__unit');
    host.dir.set('ltr');
    await fixture.whenStable();
    expect(last()).toBe('dga-unit-input__unit');
  });

  it('binds the number both ways and keeps Arabic-Indic digits as typed', async () => {
    const { fixture, host, input } = await renderHost();
    expect(input.value).toBe('72');
    type(input, '٨٠٫٥');
    input.dispatchEvent(new Event('blur'));
    await fixture.whenStable();
    expect(host.weight()).toBe(80.5);
    expect(input.value).toBe('٨٠٫٥');
    expect(host.touched()).toBe(true);
    host.weight.set(90);
    await fixture.whenStable();
    expect(input.value).toBe('90');
  });

  it('clears the value for empty text', async () => {
    const { fixture, host, input } = await renderHost();
    type(input, '');
    await fixture.whenStable();
    expect(host.weight()).toBeNull();
    expect(host.issues).toEqual([]);
  });

  it('reports unreadable text and out-of-range numbers, then recovers', async () => {
    const { fixture, host, input } = await renderHost();
    type(input, '7a');
    await fixture.whenStable();
    expect(host.weight()).toBeNull();
    expect(input.value).toBe('7a');
    expect(input.getAttribute('aria-invalid')).toBe('true');
    type(input, '500');
    await fixture.whenStable();
    type(input, '0');
    await fixture.whenStable();
    type(input, '70');
    await fixture.whenStable();
    expect(host.issues).toEqual([DgaNumberIssue.Unreadable, DgaNumberIssue.AboveMax, DgaNumberIssue.BelowMin, null]);
  });

  it('refuses decimals when integer is set', async () => {
    const fixture = TestBed.createComponent(DgaUnitInputComponent);
    fixture.componentRef.setInput('unit', 'pcs');
    fixture.componentRef.setInput('integer', true);
    await fixture.whenStable();
    const input = (fixture.nativeElement as HTMLElement).querySelector('input') as HTMLInputElement;
    expect(input.getAttribute('inputmode')).toBe('numeric');
    type(input, '2.5');
    await fixture.whenStable();
    expect(fixture.componentInstance.value()).toBeNull();
    expect(fixture.componentInstance.issue()).toBe(DgaNumberIssue.Unreadable);
  });

  it('passes the native attributes and the error description through', async () => {
    const fixture = TestBed.createComponent(DgaUnitInputComponent);
    for (const [name, value] of Object.entries({ unit: '%', ariaLabel: 'Share', name: 'share', error: 'Too high', required: true, disabled: true })) {
      fixture.componentRef.setInput(name, value);
    }
    await fixture.whenStable();
    const component = fixture.componentInstance;
    const input = (fixture.nativeElement as HTMLElement).querySelector('input') as HTMLInputElement;
    expect(input.getAttribute('aria-label')).toBe('Share');
    expect(input.name).toBe('share');
    expect(input.required).toBe(true);
    expect(input.disabled).toBe(true);
    expect(input.getAttribute('aria-describedby')).toBe(`${component.unitId} ${component.errorId}`);
  });
});
