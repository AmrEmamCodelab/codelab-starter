import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { alertIcon, DgaAlertVariant } from './alert-variant';
import { DgaAlertComponent } from './dga-alert.component';

@Component({
  imports: [DgaAlertComponent],
  template: `
    <div [attr.dir]="dir()">
      <dga-alert [variant]="variant()" [title]="title()" [dismissLabel]="dismissLabel()" (dismissed)="dismissCount = dismissCount + 1">
        <span class="body">Message body</span>
      </dga-alert>
    </div>
  `,
})
class HostComponent {
  readonly dir = signal('rtl');
  readonly variant = signal<DgaAlertVariant>(DgaAlertVariant.Info);
  readonly title = signal<string | null>('Heads up');
  readonly dismissLabel = signal<string | null>(null);
  dismissCount = 0;
}

@Component({
  imports: [DgaAlertComponent],
  template: `<dga-alert variant="success" title="Saved">Done</dga-alert>`,
})
class StaticTitleHostComponent {}

async function render(setup: (host: HostComponent) => void = () => undefined) {
  const fixture = TestBed.createComponent(HostComponent);
  setup(fixture.componentInstance);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  return { fixture, root, box: () => root.querySelector<HTMLElement>('.dga-alert') };
}

describe('DgaAlertComponent', () => {
  it.each([
    [DgaAlertVariant.Error, 'alert'],
    [DgaAlertVariant.Warning, 'alert'],
    [DgaAlertVariant.Info, 'status'],
    [DgaAlertVariant.Success, 'status'],
  ])('renders the %s variant with role %s and its icon', async (variant, role) => {
    const { box } = await render((host) => host.variant.set(variant));
    expect(box()?.getAttribute('role')).toBe(role);
    expect(box()?.dataset['variant']).toBe(variant);
    const icon = box()?.querySelector('.dga-alert__icon');
    expect(icon?.getAttribute('data-name') ?? null).toBe(alertIcon(variant));
    expect(icon?.getAttribute('aria-hidden') ?? 'true').toBe('true');
  });

  it('labels the alert by its title and projects the body', async () => {
    const { box } = await render();
    const title = box()?.querySelector('.dga-alert__title') as HTMLElement;
    expect(title.textContent).toBe('Heads up');
    expect(box()?.getAttribute('aria-labelledby')).toBe(title.id);
    expect(box()?.querySelector('.body')?.textContent).toBe('Message body');
  });

  it('omits the title and its label link when there is no title', async () => {
    const { box } = await render((host) => host.title.set(null));
    expect(box()?.querySelector('.dga-alert__title')).toBeNull();
    expect(box()?.hasAttribute('aria-labelledby')).toBe(false);
  });

  it('takes a static title as its heading without adding a tooltip to the host', async () => {
    const fixture = TestBed.createComponent(StaticTitleHostComponent);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.dga-alert__title')?.textContent).toBe('Saved');
    expect(root.querySelector('dga-alert')?.hasAttribute('title')).toBe(false);
  });

  it('shows no close button without a dismiss label', async () => {
    const { box } = await render();
    expect(box()?.querySelector('button')).toBeNull();
  });

  it.each(['rtl', 'ltr'])('closes on the named close button and emits dismissed once in %s', async (dir) => {
    const { fixture, root, box } = await render((host) => {
      host.dir.set(dir);
      host.dismissLabel.set('Close');
    });
    const close = box()?.querySelector('button') as HTMLButtonElement;
    expect(close.getAttribute('aria-label')).toBe('Close');
    close.click();
    await fixture.whenStable();
    expect(fixture.componentInstance.dismissCount).toBe(1);
    expect(box()).toBeNull();
    expect(root.querySelector('dga-alert')).not.toBeNull();
  });
});
