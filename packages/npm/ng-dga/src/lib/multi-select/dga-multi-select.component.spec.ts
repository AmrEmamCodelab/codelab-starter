import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import type { DgaSelectOption } from '../select/select-options';
import { DgaMultiSelectComponent } from './dga-multi-select.component';

@Component({
  imports: [DgaMultiSelectComponent],
  template: `
    <div [attr.dir]="dir()">
      <dga-multi-select label="Services" placeholder="Choose" removeLabel="Remove" [options]="options"
                        [(value)]="chosen" [(touched)]="touched" />
    </div>
    <button type="button" class="outside">outside</button>
  `,
})
class HostComponent {
  readonly dir = signal('rtl');
  readonly options: DgaSelectOption<number>[] = [
    { value: 1, label: 'One' },
    { value: 2, label: 'Two', disabled: true },
    { value: 3, label: 'Three' },
    { value: 4, label: 'Four' },
  ];
  readonly chosen = signal<readonly number[]>([3]);
  readonly touched = signal(false);
}

async function renderHost() {
  const fixture = TestBed.createComponent(HostComponent);
  await fixture.whenStable();
  const element = fixture.nativeElement as HTMLElement;
  document.body.appendChild(element);
  const trigger = element.querySelector('.dga-multi-select__trigger') as HTMLButtonElement;
  const listbox = element.querySelector('[role="listbox"]') as HTMLElement;
  return { fixture, host: fixture.componentInstance, element, trigger, listbox };
}

function key(target: HTMLElement, name: string): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true });
  target.dispatchEvent(event);
  return event;
}

function chipLabels(element: HTMLElement): string[] {
  return Array.from(element.querySelectorAll('.dga-multi-select__chip-label')).map((chip) => chip.textContent ?? '');
}

describe('DgaMultiSelectComponent', () => {
  it('names the trigger by label and placeholder and wires it to a hidden multi-select listbox', async () => {
    const { element, trigger, listbox } = await renderHost();
    const label = element.querySelector('.dga-multi-select__label') as HTMLElement;
    expect(trigger.getAttribute('aria-labelledby')).toBe(`${label.id} ${trigger.id}`);
    expect(trigger.getAttribute('aria-haspopup')).toBe('listbox');
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
    expect(trigger.getAttribute('aria-controls')).toBe(listbox.id);
    expect(listbox.getAttribute('aria-multiselectable')).toBe('true');
    expect(listbox.hidden).toBe(true);
  });

  it('shows the chosen options as chips with named remove buttons', async () => {
    const { element } = await renderHost();
    expect(chipLabels(element)).toEqual(['Three']);
    expect(element.querySelector('.dga-multi-select__chip-remove')?.getAttribute('aria-label')).toBe('Remove Three');
  });

  it('opens on click with focus on the list and the first chosen option active', async () => {
    const { fixture, trigger, listbox } = await renderHost();
    trigger.click();
    await fixture.whenStable();
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    expect(listbox.hidden).toBe(false);
    expect(document.activeElement).toBe(listbox);
    expect(listbox.getAttribute('aria-activedescendant')).toBe(listbox.querySelectorAll('[role="option"]')[2].id);
  });

  it('moves with the arrow keys over disabled options and toggles with Space and Enter', async () => {
    const { fixture, host, element, trigger, listbox } = await renderHost();
    key(trigger, 'ArrowDown');
    await fixture.whenStable();
    const options = Array.from(listbox.querySelectorAll('[role="option"]'));
    expect(key(listbox, 'ArrowUp').defaultPrevented).toBe(true);
    await fixture.whenStable();
    expect(listbox.getAttribute('aria-activedescendant')).toBe(options[0].id);
    key(listbox, ' ');
    key(listbox, 'End');
    key(listbox, 'Enter');
    await fixture.whenStable();
    expect(host.chosen()).toEqual([3, 1, 4]);
    expect(chipLabels(element)).toEqual(['One', 'Three', 'Four']);
    expect(options[3].getAttribute('aria-selected')).toBe('true');
    expect(options[1].getAttribute('aria-disabled')).toBe('true');
    key(listbox, 'Home');
    key(listbox, 'Enter');
    await fixture.whenStable();
    expect(host.chosen()).toEqual([3, 4]);
  });

  it('ignores clicks on a disabled option and toggles an enabled one, staying open', async () => {
    const { fixture, host, trigger, listbox } = await renderHost();
    trigger.click();
    await fixture.whenStable();
    const options = Array.from(listbox.querySelectorAll<HTMLElement>('[role="option"]'));
    options[1].click();
    options[2].click();
    await fixture.whenStable();
    expect(host.chosen()).toEqual([]);
    expect(listbox.hidden).toBe(false);
  });

  it('closes on Escape without letting it reach an enclosing dialog, and returns focus', async () => {
    const { fixture, trigger, listbox } = await renderHost();
    trigger.click();
    await fixture.whenStable();
    let reachedParent = false;
    document.body.addEventListener('keydown', () => (reachedParent = true), { once: true });
    key(listbox, 'Escape');
    await fixture.whenStable();
    expect(reachedParent).toBe(false);
    expect(listbox.hidden).toBe(true);
    expect(document.activeElement).toBe(trigger);
  });

  it('removes a chip with its button', async () => {
    const { fixture, host, element } = await renderHost();
    (element.querySelector('.dga-multi-select__chip-remove') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(host.chosen()).toEqual([]);
    expect(element.querySelector('.dga-multi-select__chips')).toBeNull();
  });

  it('closes and marks touched when focus leaves the component', async () => {
    const { fixture, host, element, trigger, listbox } = await renderHost();
    trigger.click();
    await fixture.whenStable();
    listbox.dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: trigger }));
    await fixture.whenStable();
    expect(host.touched()).toBe(false);
    listbox.dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: element.querySelector('.outside') }));
    await fixture.whenStable();
    expect(listbox.hidden).toBe(true);
    expect(host.touched()).toBe(true);
  });

  it('cannot open or remove while disabled, and shows the error', async () => {
    const fixture = TestBed.createComponent(DgaMultiSelectComponent<number>);
    fixture.componentRef.setInput('options', [{ value: 1, label: 'One' }]);
    fixture.componentRef.setInput('value', [1]);
    fixture.componentRef.setInput('disabled', true);
    fixture.componentRef.setInput('error', 'Choose at least one');
    fixture.componentRef.setInput('ariaLabel', 'Services');
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const trigger = element.querySelector('.dga-multi-select__trigger') as HTMLButtonElement;
    expect(trigger.disabled).toBe(true);
    expect(trigger.getAttribute('aria-label')).toBe('Services');
    expect(trigger.getAttribute('aria-invalid')).toBe('true');
    expect(trigger.getAttribute('aria-describedby')).toBe(fixture.componentInstance.errorId);
    expect((element.querySelector('.dga-multi-select__chip-remove') as HTMLButtonElement).disabled).toBe(true);
    fixture.componentInstance.toggleOpen();
    fixture.componentInstance.remove({ value: 1, label: 'One' });
    await fixture.whenStable();
    expect(fixture.componentInstance.open()).toBe(false);
    expect(fixture.componentInstance.value()).toEqual([1]);
  });

  it('shows the empty text when there are no options', async () => {
    const fixture = TestBed.createComponent(DgaMultiSelectComponent<number>);
    fixture.componentRef.setInput('options', []);
    fixture.componentRef.setInput('emptyText', 'Nothing to choose');
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).querySelector('.dga-multi-select__empty')?.textContent).toBe('Nothing to choose');
  });

  it('keeps the same structure in RTL and LTR', async () => {
    const { fixture, host, element } = await renderHost();
    host.dir.set('ltr');
    await fixture.whenStable();
    expect(element.querySelector('[dir]')?.getAttribute('dir')).toBe('ltr');
    expect(chipLabels(element)).toEqual(['Three']);
  });
});
