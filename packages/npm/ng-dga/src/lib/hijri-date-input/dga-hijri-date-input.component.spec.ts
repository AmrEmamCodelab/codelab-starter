import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { DgaDateIssue } from '../date-input/iso-date';
import { DgaHijriDateInputComponent } from './dga-hijri-date-input.component';
import { hijriMonthLength } from './umm-al-qura';

@Component({
  imports: [DgaHijriDateInputComponent],
  template: `
    <div [attr.dir]="dir()">
      <dga-hijri-date-input label="Birth date" dayLabel="Day" monthLabel="Month" yearLabel="Year" [locale]="locale()"
                            name="birth" [today]="today" notAfterToday [required]="true"
                            [(value)]="date" [(touched)]="touched" (issueChange)="issues.push($event)" />
    </div>
    <button type="button">outside</button>
  `,
})
class HostComponent {
  readonly dir = signal('rtl');
  readonly locale = signal('ar');
  readonly today = '2026-11-05';
  readonly date = signal('1410-06-15');
  readonly touched = signal(false);
  readonly issues: (DgaDateIssue | null)[] = [];
}

async function renderHost() {
  const fixture = TestBed.createComponent(HostComponent);
  await fixture.whenStable();
  const element = fixture.nativeElement as HTMLElement;
  const [day, month] = Array.from(element.querySelectorAll('select'));
  const year = element.querySelector('.dga-hijri__part--year') as HTMLInputElement;
  return { fixture, host: fixture.componentInstance, element, day, month, year };
}

function choose(select: HTMLSelectElement, value: string): void {
  select.value = value;
  select.dispatchEvent(new Event('change'));
}

function type(input: HTMLInputElement, text: string): void {
  input.value = text;
  input.dispatchEvent(new Event('input'));
}

describe('DgaHijriDateInputComponent', () => {
  it('lays the parts out in shrinkable grid tracks so the row never outgrows a 390px phone', async () => {
    const { element, day, month, year } = await renderHost();
    const parts = getComputedStyle(element.querySelector('.dga-hijri__parts')!);
    expect(parts.display).toBe('grid');
    expect(parts.gridTemplateColumns).toBe('minmax(0, 1fr) minmax(0, 2fr) minmax(0, 1fr)');
    for (const part of [day, month, year]) {
      expect(getComputedStyle(part).minInlineSize || getComputedStyle(part).minWidth).toMatch(/^0(px)?$/);
    }
  });

  it('is a labelled group of day, month and year with named parts', async () => {
    const { element, day, month, year } = await renderHost();
    const fieldset = element.querySelector('fieldset') as HTMLFieldSetElement;
    expect(fieldset.querySelector('legend')?.textContent).toContain('Birth date');
    expect(day.getAttribute('aria-label')).toBe('Day');
    expect(month.getAttribute('aria-label')).toBe('Month');
    expect(year.getAttribute('aria-label')).toBe('Year');
    expect(year.getAttribute('inputmode')).toBe('numeric');
    expect(day.required && month.required && year.required).toBe(true);
  });

  it('shows the model value in its parts, with month names in the locale', async () => {
    const { fixture, host, day, month, year } = await renderHost();
    expect(day.value).toBe('15');
    expect(month.value).toBe('6');
    expect(year.value).toBe('1410');
    expect(month.options[9].textContent).toBe('رمضان');
    host.locale.set('en');
    await fixture.whenStable();
    expect(month.options[9].textContent).toMatch(/^Ramadan/);
  });

  it('writes the ISO value once every part is set, accepting Arabic-Indic year digits', async () => {
    const { fixture, host, day, month, year } = await renderHost();
    host.date.set('');
    await fixture.whenStable();
    choose(day, '1');
    choose(month, '9');
    await fixture.whenStable();
    expect(host.date()).toBe('');
    expect(host.issues).toContain(DgaDateIssue.Incomplete);
    type(year, '١٤٤٦');
    await fixture.whenStable();
    expect(host.date()).toBe('1446-09-01');
    expect(year.value).toBe('١٤٤٦');
    expect(host.issues.at(-1)).toBeNull();
  });

  it('keeps a half-filled entry while the value is empty', async () => {
    const { fixture, host, day, year } = await renderHost();
    type(year, '');
    await fixture.whenStable();
    expect(host.date()).toBe('');
    expect(day.value).toBe('15');
  });

  it('offers only the real days of a 29-day month and flags an impossible 30th', async () => {
    const { fixture, host, day, month, year } = await renderHost();
    const months = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
    const shortMonth = months.find((value) => hijriMonthLength({ year: 1446, month: value }) === 29)!;
    const longMonth = months.find((value) => hijriMonthLength({ year: 1446, month: value }) === 30)!;
    host.date.set(`1446-${String(longMonth).padStart(2, '0')}-30`);
    await fixture.whenStable();
    expect(day.value).toBe('30');
    choose(month, String(shortMonth));
    await fixture.whenStable();
    expect(host.date()).toBe('');
    expect(host.issues.at(-1)).toBe(DgaDateIssue.NotADate);
    expect(day.options).toHaveLength(30);
    expect(year.getAttribute('aria-invalid')).toBe('true');
  });

  it('reports a date after today (from the caller) as AfterMax', async () => {
    const { fixture, host } = await renderHost();
    host.date.set('1449-01-01');
    await fixture.whenStable();
    expect(host.issues.at(-1)).toBe(DgaDateIssue.AfterMax);
  });

  it('marks touched only when focus leaves the whole group', async () => {
    const { fixture, host, element, day, month } = await renderHost();
    day.dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: month }));
    await fixture.whenStable();
    expect(host.touched()).toBe(false);
    day.dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: element.querySelector('button') }));
    await fixture.whenStable();
    expect(host.touched()).toBe(true);
  });

  it('describes the group by its error and carries the value for native forms', async () => {
    const fixture = TestBed.createComponent(DgaHijriDateInputComponent);
    fixture.componentRef.setInput('error', 'Required');
    fixture.componentRef.setInput('hint', 'As on the ID');
    fixture.componentRef.setInput('name', 'birth');
    fixture.componentRef.setInput('value', '1410-06-15');
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const component = fixture.componentInstance;
    expect(element.querySelector('fieldset')?.getAttribute('aria-describedby')).toBe(`${component.errorId} ${component.hintId}`);
    const hidden = element.querySelector('input[type="hidden"]') as HTMLInputElement;
    expect(hidden.name).toBe('birth');
    expect(hidden.value).toBe('1410-06-15');
  });

  it('disables every part through the fieldset', async () => {
    const fixture = TestBed.createComponent(DgaHijriDateInputComponent);
    fixture.componentRef.setInput('disabled', true);
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).querySelector('fieldset')?.disabled).toBe(true);
  });

  it('keeps day, month, year in reading order in RTL and LTR', async () => {
    const { fixture, host, element } = await renderHost();
    const order = () => Array.from(element.querySelectorAll('.dga-hijri__part')).map((part) => part.id.split('-').at(-1));
    expect(order()).toEqual(['day', 'month', 'year']);
    host.dir.set('ltr');
    await fixture.whenStable();
    expect(order()).toEqual(['day', 'month', 'year']);
  });
});
