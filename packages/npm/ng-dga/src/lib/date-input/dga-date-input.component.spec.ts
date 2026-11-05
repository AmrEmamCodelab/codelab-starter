import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { DgaDateInputComponent } from './dga-date-input.component';
import { DgaDateIssue } from './iso-date';

async function render(inputs: Record<string, unknown>) {
  const fixture = TestBed.createComponent(DgaDateInputComponent);
  for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
  await fixture.whenStable();
  const host = fixture.nativeElement as HTMLElement;
  return { fixture, host, input: host.querySelector('input') as HTMLInputElement };
}

@Component({
  imports: [DgaDateInputComponent],
  template: `
    <div [attr.dir]="dir()">
      <dga-date-input label="Date" [today]="today" notAfterToday [(value)]="date" [(touched)]="touched"
                      (issueChange)="issues.push($event)" />
    </div>
  `,
})
class HostComponent {
  readonly dir = signal('rtl');
  readonly today = '2026-11-05';
  readonly date = signal('2026-01-15');
  readonly touched = signal(false);
  readonly issues: (DgaDateIssue | null)[] = [];
}

async function renderHost() {
  const fixture = TestBed.createComponent(HostComponent);
  await fixture.whenStable();
  const input = (fixture.nativeElement as HTMLElement).querySelector('input') as HTMLInputElement;
  return { fixture, host: fixture.componentInstance, input };
}

describe('DgaDateInputComponent', () => {
  it('is a native date input linked to its visible label', async () => {
    const { host, input } = await render({ label: 'Start date' });
    expect(input.type).toBe('date');
    const label = host.querySelector('label') as HTMLLabelElement;
    expect(label.textContent?.trim()).toBe('Start date');
    expect(label.getAttribute('for')).toBe(input.id);
  });

  it('uses ariaLabel only without a visible label', async () => {
    const { input } = await render({ ariaLabel: 'Filter date' });
    expect(input.getAttribute('aria-label')).toBe('Filter date');
  });

  it('describes the input by error and hint and marks it invalid on error', async () => {
    const { fixture, input } = await render({ hint: 'Day, month, year', error: 'Required' });
    const component = fixture.componentInstance;
    expect(input.getAttribute('aria-describedby')).toBe(`${component.errorId} ${component.hintId}`);
    expect(input.getAttribute('aria-invalid')).toBe('true');
  });

  it('applies min, max and the today rule as native bounds', async () => {
    const { input } = await render({ min: '2020-01-01', max: '2030-12-31', today: '2026-11-05', notAfterToday: true });
    expect(input.getAttribute('min')).toBe('2020-01-01');
    expect(input.getAttribute('max')).toBe('2026-11-05');
  });

  it('passes required, disabled, readonly and name through', async () => {
    const { input } = await render({ required: true, disabled: true, readonly: true, name: 'start' });
    expect(input.required).toBe(true);
    expect(input.disabled).toBe(true);
    expect(input.readOnly).toBe(true);
    expect(input.name).toBe('start');
  });

  it('binds the ISO value both ways and marks touched on blur', async () => {
    const { fixture, host, input } = await renderHost();
    expect(input.value).toBe('2026-01-15');
    input.value = '2026-02-20';
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('blur'));
    await fixture.whenStable();
    expect(host.date()).toBe('2026-02-20');
    expect(host.touched()).toBe(true);
    host.date.set('2025-07-01');
    await fixture.whenStable();
    expect(input.value).toBe('2025-07-01');
  });

  it('emits AfterMax when the value passes the today cap, and null when it is fixed', async () => {
    const { fixture, host, input } = await renderHost();
    input.value = '2027-01-01';
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(host.issues).toEqual([DgaDateIssue.AfterMax]);
    expect(input.getAttribute('aria-invalid')).toBe('true');
    host.date.set('2026-11-05');
    await fixture.whenStable();
    expect(host.issues).toEqual([DgaDateIssue.AfterMax, null]);
    expect(input.hasAttribute('aria-invalid')).toBe(false);
  });

  it('reports a half-typed native value as unreadable and does not wipe it', async () => {
    const { fixture, host, input } = await renderHost();
    // jsdom does not model badInput, so stand in for a browser holding an incomplete date.
    Object.defineProperty(input, 'validity', { configurable: true, value: { badInput: true } });
    input.value = '';
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(host.date()).toBe('');
    expect(host.issues).toEqual([DgaDateIssue.Unreadable]);
  });

  it('renders the same structure in LTR and RTL (logical CSS mirrors it)', async () => {
    const { fixture, input } = await renderHost();
    expect(input.closest('[dir]')?.getAttribute('dir')).toBe('rtl');
    fixture.componentInstance.dir.set('ltr');
    await fixture.whenStable();
    expect(input.closest('[dir]')?.getAttribute('dir')).toBe('ltr');
    expect(input.value).toBe('2026-01-15');
  });
});
