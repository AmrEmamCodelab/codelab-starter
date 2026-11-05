import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { focusableWithin, initialFocusTarget, wrapFocusTarget } from './focus-trap';

describe('focus trap', () => {
  let root: HTMLElement;

  beforeEach(() => {
    root = document.createElement('div');
    root.innerHTML = `
      <button id="close">x</button>
      <div id="body">
        <input id="name" />
        <input id="hidden-type" type="hidden" />
        <button id="disabled" disabled>d</button>
        <div hidden><input id="in-hidden" /></div>
        <input id="email" />
        <span tabindex="-1" id="skipped"></span>
      </div>
      <button id="save">s</button>`;
    document.body.append(root);
  });

  afterEach(() => root.remove());

  const ids = (elements: readonly HTMLElement[]) => elements.map((element) => element.id);

  it('lists the focusable elements in DOM order, skipping disabled and hidden ones', () => {
    expect(ids(focusableWithin(root))).toEqual(['close', 'name', 'email', 'save']);
  });

  it('wraps Tab from the last element and Shift+Tab from the first', () => {
    const focusables = focusableWithin(root);
    const [first, second] = focusables;
    const last = focusables[focusables.length - 1];
    expect(wrapFocusTarget(focusables, last, false)).toBe(first);
    expect(wrapFocusTarget(focusables, first, true)).toBe(last);
    expect(wrapFocusTarget(focusables, second, false)).toBeNull();
    expect(wrapFocusTarget(focusables, second, true)).toBeNull();
  });

  it('pulls focus back in from outside the dialog', () => {
    const focusables = focusableWithin(root);
    expect(wrapFocusTarget(focusables, document.body, false)).toBe(focusables[0]);
    expect(wrapFocusTarget(focusables, null, true)).toBe(focusables[focusables.length - 1]);
    expect(wrapFocusTarget([], document.body, false)).toBeNull();
  });

  it('opens on the autofocus field, else the first field, else the dialog itself', () => {
    const body = root.querySelector<HTMLElement>('#body');
    expect(initialFocusTarget(root, body)?.id).toBe('name');
    root.querySelector('#email')?.setAttribute('autofocus', '');
    expect(initialFocusTarget(root, body)?.id).toBe('email');
    expect(initialFocusTarget(root, null)?.id).toBe('close');
  });
});
