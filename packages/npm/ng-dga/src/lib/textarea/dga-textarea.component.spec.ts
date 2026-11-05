import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { DgaTextareaComponent } from './dga-textarea.component';

async function render(inputs: Record<string, unknown>) {
  const fixture = TestBed.createComponent(DgaTextareaComponent);
  for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
  await fixture.whenStable();
  const host = fixture.nativeElement as HTMLElement;
  return { fixture, host, textarea: host.querySelector('textarea') as HTMLTextAreaElement };
}

@Component({
  imports: [DgaTextareaComponent],
  template: `
    <div [attr.dir]="dir()">
      <dga-textarea label="Notes" [maxLength]="10" counterLabel="{count} of {max}" [autoResize]="autoResize()" [(value)]="text" [(touched)]="touched" />
    </div>
  `,
})
class TwoWayHostComponent {
  readonly dir = signal('rtl');
  readonly text = signal('abc');
  readonly touched = signal(false);
  readonly autoResize = signal(false);
}

function type(textarea: HTMLTextAreaElement, text: string): void {
  textarea.value = text;
  textarea.dispatchEvent(new Event('input'));
}

describe('DgaTextareaComponent', () => {
  it('links the visible label to the textarea', async () => {
    const { host, textarea } = await render({ label: 'Notes' });
    const label = host.querySelector('label') as HTMLLabelElement;
    expect(label.textContent?.trim()).toBe('Notes');
    expect(label.getAttribute('for')).toBe(textarea.id);
    expect(textarea.hasAttribute('aria-label')).toBe(false);
  });

  it('uses ariaLabel only when there is no visible label', async () => {
    const { host, textarea } = await render({ ariaLabel: 'Comment' });
    expect(host.querySelector('label')).toBeNull();
    expect(textarea.getAttribute('aria-label')).toBe('Comment');
  });

  it('describes the textarea by its error, hint and counter and marks it invalid on error', async () => {
    const { fixture, host, textarea } = await render({ hint: 'Optional', error: 'Too short', maxLength: 20 });
    const component = fixture.componentInstance;
    expect(textarea.getAttribute('aria-describedby')).toBe(`${component.errorId} ${component.hintId} ${component.counterId}`);
    expect(textarea.getAttribute('aria-invalid')).toBe('true');
    expect(host.querySelector(`#${component.errorId}`)?.textContent).toBe('Too short');
    expect(textarea.classList).toContain('dga-field__textarea--invalid');
  });

  it('is valid with an empty error and invalid when a form marks it so', async () => {
    const empty = await render({ error: '' });
    expect(empty.textarea.hasAttribute('aria-invalid')).toBe(false);
    expect(empty.textarea.hasAttribute('aria-describedby')).toBe(false);
    const flagged = await render({ invalid: true });
    expect(flagged.textarea.getAttribute('aria-invalid')).toBe('true');
  });

  it('passes the native attributes through and defaults to four rows', async () => {
    const plain = await render({});
    expect(plain.textarea.rows).toBe(4);
    const { textarea } = await render({ name: 'notes', placeholder: 'Type here', rows: '6', maxLength: '8', required: '', readonly: true });
    expect(textarea.name).toBe('notes');
    expect(textarea.placeholder).toBe('Type here');
    expect(textarea.rows).toBe(6);
    expect(textarea.getAttribute('maxlength')).toBe('8');
    expect(textarea.required).toBe(true);
    expect(textarea.readOnly).toBe(true);
  });

  it('shows the default digit counter only when there is a maximum length', async () => {
    const without = await render({});
    expect(without.host.querySelector('.dga-field__counter')).toBeNull();
    const withMax = await render({ value: 'hello', maxLength: 50 });
    expect(withMax.host.querySelector('.dga-field__counter')?.textContent).toBe('5 / 50');
  });

  it('disables the native textarea', async () => {
    const { textarea } = await render({ disabled: true });
    expect(textarea.disabled).toBe(true);
  });

  it.each(['rtl', 'ltr'])('binds the value both ways, updates the counter and marks touched on blur in %s', async (dir) => {
    const fixture = TestBed.createComponent(TwoWayHostComponent);
    fixture.componentInstance.dir.set(dir);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const textarea = root.querySelector('textarea') as HTMLTextAreaElement;
    const counter = () => root.querySelector('.dga-field__counter')?.textContent;
    expect(textarea.value).toBe('abc');
    expect(counter()).toBe('3 of 10');
    type(textarea, 'typed text');
    textarea.dispatchEvent(new Event('blur'));
    await fixture.whenStable();
    expect(fixture.componentInstance.text()).toBe('typed text');
    expect(fixture.componentInstance.touched()).toBe(true);
    expect(counter()).toBe('10 of 10');
    fixture.componentInstance.text.set('x');
    await fixture.whenStable();
    expect(textarea.value).toBe('x');
    expect(counter()).toBe('1 of 10');
  });

  it('grows to its content when auto-resize is on, and leaves the height alone otherwise', async () => {
    const fixture = TestBed.createComponent(TwoWayHostComponent);
    await fixture.whenStable();
    const textarea = (fixture.nativeElement as HTMLElement).querySelector('textarea') as HTMLTextAreaElement;
    Object.defineProperty(textarea, 'scrollHeight', { configurable: true, get: () => textarea.value.length * 10 });
    type(textarea, 'four');
    await fixture.whenStable();
    expect(textarea.style.blockSize).toBe('');
    fixture.componentInstance.autoResize.set(true);
    await fixture.whenStable();
    expect(textarea.style.blockSize).toBe('40px');
    type(textarea, 'longer text');
    await fixture.whenStable();
    expect(textarea.style.blockSize).toBe('110px');
    expect(textarea.classList).toContain('dga-field__textarea--auto');
  });
});
