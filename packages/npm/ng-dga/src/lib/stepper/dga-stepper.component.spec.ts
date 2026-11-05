import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { DgaStepperComponent } from './dga-stepper.component';
import type { DgaStep } from './step-state';

const STEPS: readonly DgaStep[] = [{ label: 'Account' }, { label: 'Profile', description: 'Your details' }, { label: 'Review' }];

async function render(inputs: Record<string, unknown>) {
  const fixture = TestBed.createComponent(DgaStepperComponent);
  fixture.componentRef.setInput('steps', STEPS);
  for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
  await fixture.whenStable();
  const host = fixture.nativeElement as HTMLElement;
  return { fixture, host, items: Array.from(host.querySelectorAll<HTMLElement>('.dga-stepper__item')) };
}

describe('DgaStepperComponent', () => {
  it('renders an ordered list with one item per step and its state', async () => {
    const { host, items } = await render({ active: 1 });
    expect(host.querySelector('ol')).not.toBeNull();
    expect(items.map((item) => item.dataset['state'])).toEqual(['completed', 'current', 'upcoming']);
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

  it('reads the translated state text after completed and current steps only', async () => {
    const { items } = await render({ active: 1, completedLabel: 'done', currentLabel: 'now' });
    const texts = items.map((item) => item.querySelector('.dga-stepper__state')?.textContent ?? null);
    expect(texts).toEqual(['done', 'now', null]);
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
});
