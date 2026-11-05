import { afterEach, describe, expect, it, vi } from 'vitest';
import { DgaDialogKey, DgaModalDialog, focusableWithin, wrapFocusTarget } from './modal-dialog';

function buttons(count: number): HTMLButtonElement[] {
  return Array.from({ length: count }, () => document.createElement('button'));
}

function dialogWith(...children: HTMLElement[]): HTMLDialogElement {
  const dialog = document.createElement('dialog');
  dialog.append(...children);
  document.body.appendChild(dialog);
  return dialog;
}

describe('focusableWithin', () => {
  afterEach(() => document.body.replaceChildren());

  it('lists enabled controls in order and skips disabled, hidden, inert and aria-hidden ones', () => {
    const [first, disabled, last] = buttons(3);
    disabled.disabled = true;
    const hidden = document.createElement('div');
    hidden.hidden = true;
    hidden.appendChild(document.createElement('button'));
    const ariaHidden = document.createElement('div');
    ariaHidden.setAttribute('aria-hidden', 'true');
    ariaHidden.appendChild(document.createElement('input'));
    const dialog = dialogWith(first, disabled, hidden, ariaHidden, last);
    expect(focusableWithin(dialog)).toEqual([first, last]);
  });
});

describe('wrapFocusTarget', () => {
  const [first, middle, last] = buttons(3);
  const focusables = [first, middle, last];

  it('wraps Tab from the last element to the first, and from outside the list to the first', () => {
    expect(wrapFocusTarget({ focusables, current: last, backwards: false })).toBe(first);
    expect(wrapFocusTarget({ focusables, current: document.body, backwards: false })).toBe(first);
    expect(wrapFocusTarget({ focusables, current: null, backwards: false })).toBe(first);
  });

  it('wraps Shift+Tab from the first element to the last', () => {
    expect(wrapFocusTarget({ focusables, current: first, backwards: true })).toBe(last);
  });

  it('leaves moves that stay inside to the browser', () => {
    expect(wrapFocusTarget({ focusables, current: first, backwards: false })).toBeNull();
    expect(wrapFocusTarget({ focusables, current: last, backwards: true })).toBeNull();
  });

  it('has nowhere to go without focusable elements', () => {
    expect(wrapFocusTarget({ focusables: [], current: null, backwards: false })).toBeNull();
  });
});

describe('DgaModalDialog', () => {
  afterEach(() => document.body.replaceChildren());

  it('opens, focuses the initial element, and on hide returns focus to the opener', () => {
    const opener = document.createElement('button');
    document.body.appendChild(opener);
    opener.focus();
    const [first, second] = buttons(2);
    const dialog = dialogWith(first, second);
    const modal = new DgaModalDialog(document);

    modal.show(dialog, second);
    expect(dialog.hasAttribute('open')).toBe(true);
    expect(document.activeElement).toBe(second);

    modal.hide(dialog);
    expect(dialog.hasAttribute('open')).toBe(false);
    expect(document.activeElement).toBe(opener);
  });

  it('falls back to the first focusable element and ignores repeated show and hide', () => {
    const [first] = buttons(1);
    const dialog = dialogWith(first);
    const modal = new DgaModalDialog(document);
    modal.show(dialog, null);
    modal.show(dialog, null);
    expect(document.activeElement).toBe(first);
    modal.hide(dialog);
    modal.hide(dialog);
    expect(dialog.hasAttribute('open')).toBe(false);
  });

  it('maps Escape to the escape action once, even when the native cancel follows', () => {
    const dialog = dialogWith(...buttons(1));
    const modal = new DgaModalDialog(document);
    const onEscape = vi.fn();
    const escape = new KeyboardEvent('keydown', { key: DgaDialogKey.Escape, cancelable: true });
    modal.handleKeydown(dialog, escape, onEscape);
    const cancel = new Event('cancel', { cancelable: true });
    modal.handleNativeCancel(cancel, onEscape);
    expect(onEscape).toHaveBeenCalledOnce();
    expect(escape.defaultPrevented).toBe(true);
    expect(cancel.defaultPrevented).toBe(true);
  });

  it('treats a native cancel on its own as Escape', () => {
    const modal = new DgaModalDialog(document);
    const onEscape = vi.fn();
    modal.handleNativeCancel(new Event('cancel', { cancelable: true }), onEscape);
    expect(onEscape).toHaveBeenCalledOnce();
  });

  it('keeps Tab inside the dialog', () => {
    const [first, last] = buttons(2);
    const dialog = dialogWith(first, last);
    const modal = new DgaModalDialog(document);
    modal.show(dialog, last);
    const tab = new KeyboardEvent('keydown', { key: DgaDialogKey.Tab, cancelable: true });
    modal.handleKeydown(dialog, tab, vi.fn());
    expect(tab.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(first);

    const shiftTab = new KeyboardEvent('keydown', { key: DgaDialogKey.Tab, shiftKey: true, cancelable: true });
    modal.handleKeydown(dialog, shiftTab, vi.fn());
    expect(document.activeElement).toBe(last);
  });

  it('ignores other keys', () => {
    const dialog = dialogWith(...buttons(1));
    const onEscape = vi.fn();
    const enter = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true });
    new DgaModalDialog(document).handleKeydown(dialog, enter, onEscape);
    expect(onEscape).not.toHaveBeenCalled();
    expect(enter.defaultPrevented).toBe(false);
  });
});
