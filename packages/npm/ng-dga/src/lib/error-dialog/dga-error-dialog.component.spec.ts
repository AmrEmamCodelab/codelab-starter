import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DGA_CLIPBOARD, type DgaClipboard } from './dga-clipboard';
import { DGA_COPY_FEEDBACK_MS } from './error-reference';
import { DgaErrorDialogComponent } from './dga-error-dialog.component';

const CODE = '9TQ4M-2HXRB';

@Component({
  imports: [DgaErrorDialogComponent],
  template: `
    <div [attr.dir]="dir()">
      <button type="button" class="opener">Open</button>
      <dga-error-dialog
        [open]="open()"
        title="Something went wrong"
        message="Try again later."
        referenceLabel="Reference code"
        [referenceCode]="code()"
        [copyLabel]="copyLabel()"
        copiedLabel="Copied"
        [copyAriaLabel]="copyAriaLabel()"
        closeLabel="Close"
        (closed)="closedCount = closedCount + 1"
        (copied)="copiedCodes.push($event)"
      />
    </div>
  `,
})
class HostComponent {
  readonly dir = signal('rtl');
  readonly open = signal(true);
  readonly code = signal<string | null>(CODE);
  readonly copyLabel = signal<string | null>('Copy');
  readonly copyAriaLabel = signal<string | null>(null);
  closedCount = 0;
  readonly copiedCodes: string[] = [];
}

describe('DgaErrorDialogComponent', () => {
  let clipboard: { write: ReturnType<typeof vi.fn<DgaClipboard['write']>> };

  beforeEach(() => {
    clipboard = { write: vi.fn<DgaClipboard['write']>().mockResolvedValue(true) };
    TestBed.configureTestingModule({ providers: [{ provide: DGA_CLIPBOARD, useValue: clipboard }] });
  });

  afterEach(() => {
    vi.useRealTimers();
    document.body.replaceChildren();
  });

  async function render(setup: (host: HostComponent) => void = () => undefined) {
    const fixture = TestBed.createComponent(HostComponent);
    document.body.appendChild(fixture.nativeElement);
    setup(fixture.componentInstance);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    return {
      fixture,
      root,
      dialog: () => root.querySelector('dialog') as HTMLDialogElement,
      closeButton: () => root.querySelector('.dga-error-dialog__close button') as HTMLButtonElement,
      copyButton: () => root.querySelector<HTMLButtonElement>('.dga-error-dialog__copy button'),
    };
  }

  it.each(['rtl', 'ltr'])('opens as a named, described alertdialog with the close button focused (%s)', async (dir) => {
    const { dialog, closeButton, root } = await render((host) => host.dir.set(dir));
    expect(dialog().hasAttribute('open')).toBe(true);
    expect(dialog().getAttribute('role')).toBe('alertdialog');
    expect(root.querySelector(`#${dialog().getAttribute('aria-labelledby')}`)?.textContent).toBe('Something went wrong');
    expect(root.querySelector(`#${dialog().getAttribute('aria-describedby')}`)?.textContent).toBe('Try again later.');
    expect(document.activeElement).toBe(closeButton());
  });

  it('shows the reference code left-to-right under its label', async () => {
    const { root } = await render();
    const code = root.querySelector('.dga-error-dialog__reference-code') as HTMLElement;
    expect(code.textContent).toBe(CODE);
    expect(code.getAttribute('dir')).toBe('ltr');
    expect(code.hasAttribute('data-fixed-direction')).toBe(true);
    expect(root.querySelector('.dga-error-dialog__reference-label')?.textContent).toBe('Reference code');
  });

  it('marks the code and the copy button for browser tests', async () => {
    const { root } = await render();
    expect(root.querySelector('[data-testid="error-reference-code"]')?.textContent).toBe(CODE);
    expect(root.querySelector('[data-testid="error-reference-copy"] button')).not.toBeNull();
  });

  it('names the copy button with the aria label when given, else by its visible label', async () => {
    const plain = await render();
    expect(plain.copyButton()?.hasAttribute('aria-label')).toBe(false);
    expect(plain.copyButton()?.textContent).toContain('Copy');
    document.body.replaceChildren();
    const named = await render((host) => host.copyAriaLabel.set('Copy the reference code'));
    expect(named.copyButton()?.getAttribute('aria-label')).toBe('Copy the reference code');
  });

  it('hides the test-marked code when there is no code', async () => {
    const { root } = await render((host) => host.code.set(null));
    expect(root.querySelector('[data-testid="error-reference-code"]')).toBeNull();
    expect(root.querySelector('[data-testid="error-reference-copy"]')).toBeNull();
  });

  it('hides the reference block without a code, and the copy button without a copy label', async () => {
    const withoutCopy = await render((host) => host.copyLabel.set(null));
    expect(withoutCopy.copyButton()).toBeNull();
    expect(withoutCopy.root.querySelector('.dga-error-dialog__reference-code')).not.toBeNull();
    document.body.replaceChildren();
    const withoutCode = await render((host) => host.code.set(null));
    expect(withoutCode.root.querySelector('.dga-error-dialog__reference')).toBeNull();
  });

  it('emits closed on the close button and on Escape', async () => {
    const { fixture, dialog, closeButton } = await render();
    closeButton().click();
    dialog().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    expect(fixture.componentInstance.closedCount).toBe(2);
  });

  it('keeps Tab inside the dialog', async () => {
    const { dialog, closeButton, copyButton } = await render();
    closeButton().focus();
    dialog().dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(copyButton());
  });

  it('returns focus to the opener when it closes', async () => {
    const { fixture, root, dialog } = await render((host) => host.open.set(false));
    const opener = root.querySelector('.opener') as HTMLButtonElement;
    opener.focus();
    fixture.componentInstance.open.set(true);
    await fixture.whenStable();
    expect(document.activeElement).not.toBe(opener);
    fixture.componentInstance.open.set(false);
    await fixture.whenStable();
    expect(dialog().hasAttribute('open')).toBe(false);
    expect(document.activeElement).toBe(opener);
  });

  it('copies the code, announces it, and reverts the label after the feedback time', async () => {
    const { fixture, root, copyButton } = await render();
    vi.useFakeTimers();
    copyButton()?.click();
    await vi.waitFor(() => expect(fixture.componentInstance.copiedCodes).toEqual([CODE]));
    fixture.detectChanges();
    expect(clipboard.write).toHaveBeenCalledWith(CODE);
    expect(copyButton()?.textContent).toContain('Copied');
    expect(root.querySelector('.dga-error-dialog__status')?.getAttribute('aria-live')).toBe('polite');
    expect(root.querySelector('.dga-error-dialog__status')?.textContent?.trim()).toBe('Copied');
    vi.advanceTimersByTime(DGA_COPY_FEEDBACK_MS);
    fixture.detectChanges();
    expect(copyButton()?.textContent).toContain('Copy');
    expect(root.querySelector('.dga-error-dialog__status')?.textContent?.trim()).toBe('');
  });

  it('shows no confirmation when the clipboard refuses', async () => {
    clipboard.write.mockResolvedValue(false);
    const { fixture, copyButton } = await render();
    copyButton()?.click();
    await vi.waitFor(() => expect(clipboard.write).toHaveBeenCalled());
    await fixture.whenStable();
    expect(fixture.componentInstance.copiedCodes).toEqual([]);
    expect(copyButton()?.textContent).not.toContain('Copied');
  });
});
