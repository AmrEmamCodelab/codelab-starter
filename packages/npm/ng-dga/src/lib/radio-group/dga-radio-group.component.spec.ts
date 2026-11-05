import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { type DgaRadioOption, type DgaRadioValue, DgaRadioGroupComponent } from './dga-radio-group.component';

const OPTIONS: readonly DgaRadioOption[] = [
  { value: 'ar', label: 'Arabic' },
  { value: 'en', label: 'English', hint: 'Left to right' },
  { value: 'fr', label: 'French', disabled: true },
];

async function render(inputs: Record<string, unknown>) {
  const fixture = TestBed.createComponent(DgaRadioGroupComponent);
  fixture.componentRef.setInput('options', OPTIONS);
  for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
  await fixture.whenStable();
  const host = fixture.nativeElement as HTMLElement;
  return {
    fixture,
    host,
    group: host.querySelector('[role="radiogroup"]') as HTMLElement,
    radios: Array.from(host.querySelectorAll<HTMLInputElement>('input[type="radio"]')),
  };
}

@Component({
  imports: [DgaRadioGroupComponent],
  template: `<dga-radio-group ariaLabel="Language" [options]="options" [(value)]="language" [(touched)]="touched" />`,
})
class TwoWayHostComponent {
  readonly options = OPTIONS;
  readonly language = signal<DgaRadioValue | null>('ar');
  readonly touched = signal(false);
}

describe('DgaRadioGroupComponent', () => {
  it('is a radiogroup named by its visible label', async () => {
    const { fixture, host, group } = await render({ label: 'Language' });
    const labelId = fixture.componentInstance.labelId;
    expect(group.getAttribute('aria-labelledby')).toBe(labelId);
    expect(host.querySelector(`#${labelId}`)?.textContent?.trim()).toBe('Language');
    expect(group.hasAttribute('aria-label')).toBe(false);
  });

  it('uses ariaLabel when there is no visible label', async () => {
    const { group } = await render({ ariaLabel: 'Language' });
    expect(group.getAttribute('aria-label')).toBe('Language');
    expect(group.hasAttribute('aria-labelledby')).toBe(false);
  });

  it('renders one native radio per option sharing a generated name', async () => {
    const { radios } = await render({});
    expect(radios).toHaveLength(OPTIONS.length);
    expect(new Set(radios.map((radio) => radio.name)).size).toBe(1);
    expect(radios[0].name.startsWith('dga-radio-group-')).toBe(true);
    expect(radios.map((radio) => radio.value)).toEqual(['ar', 'en', 'fr']);
  });

  it('uses a given name and checks the selected value', async () => {
    const { radios } = await render({ name: 'lang', value: 'en' });
    expect(radios.every((radio) => radio.name === 'lang')).toBe(true);
    expect(radios.map((radio) => radio.checked)).toEqual([false, true, false]);
  });

  it('disables a disabled option, or every option when the group is disabled', async () => {
    const partial = await render({});
    expect(partial.radios.map((radio) => radio.disabled)).toEqual([false, false, true]);
    const all = await render({ disabled: true });
    expect(all.radios.every((radio) => radio.disabled)).toBe(true);
    expect(all.group.getAttribute('aria-disabled')).toBe('true');
  });

  it('describes the group by its error and hint and flags required and invalid', async () => {
    const { fixture, group, host } = await render({ error: 'Pick one', hint: 'Any language', required: true });
    const component = fixture.componentInstance;
    expect(group.getAttribute('aria-describedby')).toBe(`${component.errorId} ${component.hintId}`);
    expect(group.getAttribute('aria-invalid')).toBe('true');
    expect(group.getAttribute('aria-required')).toBe('true');
    expect(host.querySelector('.dga-radio__hint')?.textContent).toBe('Left to right');
  });

  it('lays the options out by orientation', async () => {
    const { group } = await render({ orientation: 'horizontal' });
    expect(group.getAttribute('data-orientation')).toBe('horizontal');
  });

  it('ignores selecting a disabled or already selected option', async () => {
    const { fixture } = await render({ value: 'ar' });
    const component = fixture.componentInstance;
    component.select(OPTIONS[2]);
    component.select(OPTIONS[0]);
    expect(component.value()).toBe('ar');
  });

  it('selects both ways and marks touched when focus leaves', async () => {
    const fixture = TestBed.createComponent(TwoWayHostComponent);
    await fixture.whenStable();
    const radios = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLInputElement>('input'));
    expect(radios[0].checked).toBe(true);
    radios[1].click();
    radios[1].dispatchEvent(new Event('blur'));
    await fixture.whenStable();
    expect(fixture.componentInstance.language()).toBe('en');
    expect(fixture.componentInstance.touched()).toBe(true);
    fixture.componentInstance.language.set(null);
    await fixture.whenStable();
    expect(radios.some((radio) => radio.checked)).toBe(false);
  });
});
