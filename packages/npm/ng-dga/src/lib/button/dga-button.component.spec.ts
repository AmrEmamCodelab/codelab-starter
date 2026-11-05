import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { DgaButtonComponent } from './dga-button.component';

@Component({
  imports: [DgaButtonComponent],
  template: `<dga-button [loading]="loading()" [disabled]="disabled()" (click)="clicked()">Save</dga-button>`,
})
class HostComponent {
  readonly loading = input(false);
  readonly disabled = input(false);
  readonly clicked = vi.fn();
}

async function render(inputs: Record<string, unknown>) {
  const fixture = TestBed.createComponent(DgaButtonComponent);
  for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
  await fixture.whenStable();
  const host = fixture.nativeElement as HTMLElement;
  return { fixture, host, button: host.querySelector('button') as HTMLButtonElement };
}

describe('DgaButtonComponent', () => {
  it('renders a native button of type button with the default variant and size', async () => {
    const { button } = await render({});
    expect(button.type).toBe('button');
    expect(button.dataset['variant']).toBe('primary');
    expect(button.dataset['size']).toBe('lg');
  });

  it('exposes the variant matrix as data attributes', async () => {
    const { button } = await render({ variant: 'secondary-outline', size: 'sm', destructive: '', onColor: true, selected: true });
    expect(button.dataset['variant']).toBe('secondary-outline');
    expect(button.dataset['size']).toBe('sm');
    expect(button.hasAttribute('data-destructive')).toBe(true);
    expect(button.hasAttribute('data-on-color')).toBe(true);
    expect(button.hasAttribute('data-selected')).toBe(true);
  });

  it('is disabled and busy while loading, with a decorative spinner', async () => {
    const { fixture, button } = await render({ loading: true });
    expect(fixture.componentInstance.isDisabled()).toBe(true);
    expect(button.disabled).toBe(true);
    expect(button.getAttribute('aria-busy')).toBe('true');
    expect(button.querySelector('.dga-btn__spinner')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('is a toggle button only when pressed is set', async () => {
    const plain = await render({});
    expect(plain.button.hasAttribute('aria-pressed')).toBe(false);
    const toggle = await render({ pressed: false });
    expect(toggle.button.getAttribute('aria-pressed')).toBe('false');
  });

  it('hides the projected label when icon-only and names the button by label', async () => {
    const { button } = await render({ iconOnly: true, leadIcon: 'close', label: 'Close' });
    expect(button.querySelector('.dga-btn__label')).toBeNull();
    expect(button.getAttribute('aria-label')).toBe('Close');
    expect(button.querySelector('dga-icon')?.getAttribute('data-name')).toBe('close');
  });

  it('renders lead and trail icons and the submit type', async () => {
    const { button } = await render({ leadIcon: 'check', trailIcon: 'arrow-end', type: 'submit' });
    expect(Array.from(button.querySelectorAll('dga-icon')).map((icon) => icon.getAttribute('data-name'))).toEqual(['check', 'arrow-end']);
    expect(button.type).toBe('submit');
  });

  it('stretches the host when fullWidth', async () => {
    const { host } = await render({ fullWidth: true });
    expect(host.classList.contains('dga-button--full')).toBe(true);
  });

  it('projects the label and emits no click while disabled or loading', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
    const button = (fixture.nativeElement as HTMLElement).querySelector('button') as HTMLButtonElement;
    expect(button.textContent?.trim()).toBe('Save');
    button.click();
    expect(fixture.componentInstance.clicked).toHaveBeenCalledTimes(1);
    fixture.componentRef.setInput('loading', true);
    await fixture.whenStable();
    button.click();
    expect(fixture.componentInstance.clicked).toHaveBeenCalledTimes(1);
  });
});
