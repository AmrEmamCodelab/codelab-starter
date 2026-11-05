import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { DgaConfirmDialogVariant } from './confirm-dialog-variant';
import { DgaConfirmDialogComponent } from './dga-confirm-dialog.component';

@Component({
  imports: [DgaConfirmDialogComponent],
  template: `
    <div [attr.dir]="dir()">
      <button type="button" class="opener">Open</button>
      <dga-confirm-dialog
        [open]="open()"
        [variant]="variant()"
        [busy]="busy()"
        title="Delete the record?"
        [message]="message()"
        confirmLabel="Delete"
        cancelLabel="Keep"
        (confirmed)="confirmedCount = confirmedCount + 1"
        (cancelled)="cancelledCount = cancelledCount + 1"
      >
        <span class="extra">Extra body</span>
      </dga-confirm-dialog>
    </div>
  `,
})
class HostComponent {
  readonly dir = signal('rtl');
  readonly open = signal(false);
  readonly busy = signal(false);
  readonly variant = signal<DgaConfirmDialogVariant>(DgaConfirmDialogVariant.Default);
  readonly message = signal<string | null>('This cannot be undone.');
  confirmedCount = 0;
  cancelledCount = 0;
}

async function render(setup: (host: HostComponent) => void = () => undefined) {
  const fixture = TestBed.createComponent(HostComponent);
  document.body.appendChild(fixture.nativeElement);
  setup(fixture.componentInstance);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  const dialog = () => root.querySelector('dialog') as HTMLDialogElement;
  const confirmButton = () => root.querySelector('.dga-confirm-dialog__confirm button') as HTMLButtonElement;
  const cancelButton = () => root.querySelector('.dga-confirm-dialog__cancel button') as HTMLButtonElement;
  const opener = () => root.querySelector('.opener') as HTMLButtonElement;
  const setOpen = async (open: boolean) => {
    fixture.componentInstance.open.set(open);
    await fixture.whenStable();
  };
  return { fixture, root, dialog, confirmButton, cancelButton, opener, setOpen };
}

function keydown(target: Element, key: string, shiftKey = false): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key, shiftKey, bubbles: true, cancelable: true });
  target.dispatchEvent(event);
  return event;
}

describe('DgaConfirmDialogComponent', () => {
  afterEach(() => document.body.replaceChildren());

  it('is a closed alertdialog until opened, named by its title and described by its message', async () => {
    const { dialog, setOpen, root } = await render();
    expect(dialog().hasAttribute('open')).toBe(false);
    await setOpen(true);
    expect(dialog().hasAttribute('open')).toBe(true);
    expect(dialog().getAttribute('role')).toBe('alertdialog');
    expect(dialog().getAttribute('aria-modal')).toBe('true');
    const title = root.querySelector('h2') as HTMLElement;
    expect(title.textContent).toBe('Delete the record?');
    expect(dialog().getAttribute('aria-labelledby')).toBe(title.id);
    const message = root.querySelector('.dga-confirm-dialog__message') as HTMLElement;
    expect(dialog().getAttribute('aria-describedby')).toBe(message.id);
    expect(root.querySelector('.extra')?.textContent).toBe('Extra body');
  });

  it('drops the description link without a message', async () => {
    const { dialog, setOpen } = await render((host) => host.message.set(null));
    await setOpen(true);
    expect(dialog().hasAttribute('aria-describedby')).toBe(false);
  });

  it.each(['rtl', 'ltr'])('focuses Confirm on open, emits on the buttons and returns focus to the opener (%s)', async (dir) => {
    const { fixture, confirmButton, cancelButton, opener, setOpen } = await render((host) => host.dir.set(dir));
    opener().focus();
    await setOpen(true);
    expect(document.activeElement).toBe(confirmButton());
    confirmButton().click();
    cancelButton().click();
    expect(fixture.componentInstance.confirmedCount).toBe(1);
    expect(fixture.componentInstance.cancelledCount).toBe(1);
    await setOpen(false);
    expect(document.activeElement).toBe(opener());
  });

  it('focuses Cancel first for the danger variant and marks Confirm destructive', async () => {
    const { dialog, cancelButton, confirmButton, setOpen } = await render((host) => host.variant.set(DgaConfirmDialogVariant.Danger));
    await setOpen(true);
    expect(document.activeElement).toBe(cancelButton());
    expect(dialog().dataset['variant']).toBe('danger');
    expect(confirmButton().hasAttribute('data-destructive')).toBe(true);
  });

  it('cancels on Escape once', async () => {
    const { fixture, dialog, setOpen } = await render();
    await setOpen(true);
    const event = keydown(dialog(), 'Escape');
    dialog().dispatchEvent(new Event('cancel', { cancelable: true }));
    expect(event.defaultPrevented).toBe(true);
    expect(fixture.componentInstance.cancelledCount).toBe(1);
  });

  it.each(['rtl', 'ltr'])('keeps Tab inside the dialog (%s)', async (dir) => {
    const { dialog, confirmButton, cancelButton, setOpen } = await render((host) => host.dir.set(dir));
    await setOpen(true);
    confirmButton().focus();
    keydown(dialog(), 'Tab');
    expect(document.activeElement).toBe(cancelButton());
    keydown(dialog(), 'Tab', true);
    expect(document.activeElement).toBe(confirmButton());
  });

  it('emits nothing while busy and marks the dialog busy', async () => {
    const { fixture, dialog, confirmButton, setOpen } = await render((host) => host.busy.set(true));
    await setOpen(true);
    expect(dialog().getAttribute('aria-busy')).toBe('true');
    expect(confirmButton().disabled).toBe(true);
    keydown(dialog(), 'Escape');
    fixture.componentInstance.confirmedCount = 0;
    (fixture.debugElement.query((element) => element.name === 'dga-confirm-dialog').componentInstance as DgaConfirmDialogComponent).confirm();
    expect(fixture.componentInstance.confirmedCount).toBe(0);
    expect(fixture.componentInstance.cancelledCount).toBe(0);
  });
});
