import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { DgaStepperComponent } from './dga-stepper.component';
import type { DgaStep, DgaStepperOrientation } from './step-state';

const STEPS: readonly DgaStep[] = [{ label: 'Account' }, { label: 'Profile', description: 'Your details' }, { label: 'Review' }];

async function render(inputs: Record<string, unknown>) {
  const fixture = TestBed.createComponent(DgaStepperComponent);
  fixture.componentRef.setInput('steps', STEPS);
  for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
  await fixture.whenStable();
  const host = fixture.nativeElement as HTMLElement;
  return { fixture, host, items: Array.from(host.querySelectorAll<HTMLElement>('.dga-stepper__item')) };
}

@Component({
  imports: [DgaStepperComponent],
  template: `
    <div [attr.dir]="dir()">
      <dga-stepper
        navigable
        [steps]="steps()"
        [active]="active()"
        [linear]="linear()"
        [orientation]="orientation()"
        label="Progress"
        (stepSelect)="selected.push($event)"
      />
    </div>
  `,
})
class NavigableHostComponent {
  readonly steps = signal<readonly DgaStep[]>([
    { label: 'One' },
    { label: 'Two' },
    { label: 'Three', disabled: true },
    { label: 'Four' },
  ]);
  readonly active = signal(1);
  readonly linear = signal(false);
  readonly orientation = signal<DgaStepperOrientation>('horizontal');
  readonly dir = signal('ltr');
  readonly selected: number[] = [];
}

async function renderNavigable(setup?: (host: NavigableHostComponent) => void) {
  const fixture = TestBed.createComponent(NavigableHostComponent);
  setup?.(fixture.componentInstance);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  const buttons = () => Array.from(root.querySelectorAll<HTMLButtonElement>('button.dga-stepper__step'));
  return { fixture, root, buttons, selected: fixture.componentInstance.selected };
}

function press(element: HTMLElement, key: string): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
  element.dispatchEvent(event);
  return event;
}

describe('DgaStepperComponent (progress only)', () => {
  it('renders an ordered list with one item per step and its state', async () => {
    const { host, items } = await render({ active: 1 });
    expect(host.querySelector('ol')).not.toBeNull();
    expect(items.map((item) => item.dataset['state'])).toEqual(['completed', 'current', 'upcoming']);
    expect(host.querySelector('button')).toBeNull();
  });

  it('marks only the current step with aria-current="step"', async () => {
    const { items } = await render({ active: 1 });
    expect(items.map((item) => item.getAttribute('aria-current'))).toEqual([null, 'step', null]);
  });

  it('shows a check for completed steps and the number for the others, hidden from assistive technology', async () => {
    const { items } = await render({ active: 1 });
    const bullets = items.map((item) => item.querySelector('.dga-stepper__bullet') as HTMLElement);
    expect(bullets[0].querySelector('dga-icon')?.getAttribute('data-name')).toBe('check');
    expect(bullets[2].textContent?.trim()).toBe('3');
    expect(bullets.every((bullet) => bullet.getAttribute('aria-hidden') === 'true')).toBe(true);
  });

  it('reads the translated state text after completed, current and invalid steps only', async () => {
    const steps: readonly DgaStep[] = [{ label: 'a' }, { label: 'b' }, { label: 'c', invalid: true }, { label: 'd' }];
    const { items } = await render({ steps, active: 1, completedLabel: 'done', currentLabel: 'now', invalidLabel: 'fix' });
    const texts = items.map((item) => item.querySelector('.dga-stepper__state')?.textContent ?? null);
    expect(texts).toEqual(['done', 'now', 'fix', null]);
  });

  it('shows an alert bullet on an invalid step', async () => {
    const steps: readonly DgaStep[] = [{ label: 'a', invalid: true }, { label: 'b' }];
    const { items } = await render({ steps, active: 1 });
    expect(items[0].dataset['state']).toBe('invalid');
    expect(items[0].querySelector('dga-icon')?.getAttribute('data-name')).toBe('alert');
  });

  it('renders descriptions, connectors between steps and fills the completed ones', async () => {
    const { host, items } = await render({ active: 1 });
    expect(items[1].querySelector('.dga-stepper__description')?.textContent).toBe('Your details');
    const lines = Array.from(host.querySelectorAll('.dga-stepper__line'));
    expect(lines).toHaveLength(STEPS.length - 1);
    expect(lines.map((line) => line.hasAttribute('data-filled'))).toEqual([true, false]);
  });

  it('names the list and clamps an out-of-range active step', async () => {
    const { fixture, host } = await render({ active: 99, label: 'Progress' });
    expect(host.querySelector('ol')?.getAttribute('aria-label')).toBe('Progress');
    expect(fixture.componentInstance.activeIndex()).toBe(STEPS.length);
  });

  it('non-linear: marks only flagged steps completed (the former vertical app-stepper)', async () => {
    const steps: readonly DgaStep[] = [{ label: 'a', completed: true }, { label: 'b' }, { label: 'c' }];
    const { items } = await render({ steps, active: 2, linear: false });
    expect(items.map((item) => item.dataset['state'])).toEqual(['completed', 'upcoming', 'current']);
  });

  it('exposes orientation and compact mode on the host for styling', async () => {
    const { host } = await render({ orientation: 'vertical', compact: true });
    expect(host.getAttribute('data-orientation')).toBe('vertical');
    expect(host.classList.contains('dga-stepper--compact')).toBe(true);
  });

  it('keeps every label in the DOM in compact mode (hidden visually, still read)', async () => {
    const { host } = await render({ compact: true, active: 1 });
    expect(Array.from(host.querySelectorAll('.dga-stepper__label')).map((label) => label.textContent)).toEqual([
      'Account',
      'Profile',
      'Review',
    ]);
  });
});

describe('DgaStepperComponent (navigable)', () => {
  it('renders each step as a button and moves aria-current onto the current button', async () => {
    const { root, buttons } = await renderNavigable();
    expect(buttons()).toHaveLength(4);
    expect(buttons().map((button) => button.getAttribute('aria-current'))).toEqual([null, 'step', null, null]);
    expect(root.querySelector('li[aria-current]')).toBeNull();
  });

  it('disables steps that cannot be selected and keeps only the current step in the Tab order', async () => {
    const { buttons } = await renderNavigable();
    expect(buttons().map((button) => button.disabled)).toEqual([false, false, true, false]);
    expect(buttons().map((button) => button.getAttribute('tabindex'))).toEqual(['-1', '0', '-1', '-1']);
  });

  it('emits stepSelect for a selectable step, never for the current one', async () => {
    const { buttons, selected } = await renderNavigable();
    buttons()[0].click();
    buttons()[1].click();
    buttons()[3].click();
    expect(selected).toEqual([0, 3]);
  });

  it('linear: only earlier steps are selectable', async () => {
    const { buttons, selected } = await renderNavigable((host) => host.linear.set(true));
    expect(buttons().map((button) => button.disabled)).toEqual([false, false, true, true]);
    buttons()[0].click();
    expect(selected).toEqual([0]);
  });

  it('moves focus with the arrow keys, skipping disabled steps, and stops at the ends (LTR)', async () => {
    const { buttons } = await renderNavigable();
    buttons()[1].focus();
    const event = press(buttons()[1], 'ArrowRight');
    expect(event.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(buttons()[3]);
    press(buttons()[3], 'ArrowRight');
    expect(document.activeElement).toBe(buttons()[3]);
    press(buttons()[3], 'Home');
    expect(document.activeElement).toBe(buttons()[0]);
    press(buttons()[0], 'End');
    expect(document.activeElement).toBe(buttons()[3]);
  });

  it('mirrors the arrow keys in RTL', async () => {
    const { buttons } = await renderNavigable((host) => host.dir.set('rtl'));
    buttons()[1].focus();
    press(buttons()[1], 'ArrowLeft');
    expect(document.activeElement).toBe(buttons()[3]);
    press(buttons()[3], 'ArrowRight');
    expect(document.activeElement).toBe(buttons()[1]);
  });

  it('uses Up and Down in a vertical stepper and ignores Left and Right', async () => {
    const { buttons } = await renderNavigable((host) => host.orientation.set('vertical'));
    buttons()[1].focus();
    const ignored = press(buttons()[1], 'ArrowRight');
    expect(ignored.defaultPrevented).toBe(false);
    press(buttons()[1], 'ArrowUp');
    expect(document.activeElement).toBe(buttons()[0]);
    press(buttons()[0], 'ArrowDown');
    expect(document.activeElement).toBe(buttons()[1]);
  });
});
