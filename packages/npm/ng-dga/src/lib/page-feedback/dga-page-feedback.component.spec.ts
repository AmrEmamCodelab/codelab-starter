import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { DgaPageFeedbackComponent } from './dga-page-feedback.component';
import { DgaPageFeedbackKind, type DgaPageFeedbackState } from './page-feedback-state';

@Component({
  imports: [DgaPageFeedbackComponent],
  template: `
    <div [attr.dir]="dir()">
      <button type="button" class="action">Save</button>
      <dga-page-feedback [state]="state()" [dismissLabel]="dismissLabel()" [moveFocus]="moveFocus()" (dismissed)="dismissedCount = dismissedCount + 1" />
    </div>
  `,
})
class HostComponent {
  readonly dir = signal('rtl');
  readonly state = signal<DgaPageFeedbackState | null>(null);
  readonly dismissLabel = signal<string | null>(null);
  readonly moveFocus = signal(true);
  dismissedCount = 0;
}

async function render(setup: (host: HostComponent) => void = () => undefined) {
  const fixture = TestBed.createComponent(HostComponent);
  document.body.appendChild(fixture.nativeElement);
  setup(fixture.componentInstance);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  const setState = async (state: DgaPageFeedbackState | null) => {
    fixture.componentInstance.state.set(state);
    await fixture.whenStable();
  };
  return { fixture, root, banner: () => root.querySelector<HTMLElement>('.dga-page-feedback'), setState };
}

describe('DgaPageFeedbackComponent', () => {
  afterEach(() => document.body.replaceChildren());

  it('renders nothing without a state', async () => {
    const { banner } = await render();
    expect(banner()).toBeNull();
  });

  it.each([
    [DgaPageFeedbackKind.Error, 'alert'],
    [DgaPageFeedbackKind.Success, 'status'],
    [DgaPageFeedbackKind.Info, 'status'],
  ])('renders %s with role %s, its icon and the message', async (kind, role) => {
    const { banner, setState } = await render();
    await setState({ kind, message: 'The outcome.' });
    expect(banner()?.getAttribute('role')).toBe(role);
    expect(banner()?.dataset['kind']).toBe(kind);
    expect(banner()?.querySelector('.dga-page-feedback__icon')?.getAttribute('data-name')).toBe(kind);
    expect(banner()?.querySelector('.dga-page-feedback__message')?.textContent).toBe('The outcome.');
  });

  it('labels the banner by its title when there is one', async () => {
    const { banner, setState } = await render();
    await setState({ kind: DgaPageFeedbackKind.Success, title: 'Saved', message: 'Your changes are saved.' });
    const title = banner()?.querySelector('.dga-page-feedback__title') as HTMLElement;
    expect(title.textContent).toBe('Saved');
    expect(banner()?.getAttribute('aria-labelledby')).toBe(title.id);
  });

  it.each(['rtl', 'ltr'])('moves focus to the banner on every new state (%s)', async (dir) => {
    const { root, banner, setState } = await render((host) => host.dir.set(dir));
    const action = root.querySelector('.action') as HTMLButtonElement;
    action.focus();
    await setState({ kind: DgaPageFeedbackKind.Error, message: 'Failed.' });
    expect(banner()?.getAttribute('tabindex')).toBe('-1');
    expect(document.activeElement).toBe(banner());
    action.focus();
    await setState({ kind: DgaPageFeedbackKind.Error, message: 'Failed.' });
    expect(document.activeElement).toBe(banner());
  });

  it('leaves focus alone when moveFocus is off', async () => {
    const { root, setState } = await render((host) => host.moveFocus.set(false));
    const action = root.querySelector('.action') as HTMLButtonElement;
    action.focus();
    await setState({ kind: DgaPageFeedbackKind.Info, message: 'Note.' });
    expect(document.activeElement).toBe(action);
  });

  it('offers a named close button only with a dismiss label, and emits dismissed', async () => {
    const { fixture, banner, setState } = await render();
    await setState({ kind: DgaPageFeedbackKind.Info, message: 'Note.' });
    expect(banner()?.querySelector('button')).toBeNull();
    fixture.componentInstance.dismissLabel.set('Close');
    await fixture.whenStable();
    const close = banner()?.querySelector('button') as HTMLButtonElement;
    expect(close.getAttribute('aria-label')).toBe('Close');
    close.click();
    expect(fixture.componentInstance.dismissedCount).toBe(1);
  });
});
