import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { DgaTextInputComponent } from '@codelab/ng-dga';
import { parseApiError, type ApiError } from '@codelab/ng-security';
import { beforeEach, describe, expect, it } from 'vitest';
import { patternTestProviders, textOf } from '../patterns.testing';
import { EditorDialogPatternComponent } from './editor-dialog-pattern.component';
import { FieldErrorPipe } from './field-error.pipe';
import { fieldErrorsFrom, type FieldErrorMap } from './field-errors';

const BAD_REQUEST = 400;
const CONFLICT = 409;

/** Living documentation: how a screen uses the editor dialog. */
@Component({
  imports: [EditorDialogPatternComponent, DgaTextInputComponent, FieldErrorPipe],
  template: `
    <button type="button" class="opener" data-public-action (click)="open.set(true)">open</button>
    <app-editor-dialog-pattern
      titleKey="admin.users.edit-title"
      [open]="open()"
      [busy]="busy()"
      [error]="error()"
      [translatedCodes]="translatedCodes()"
      [fieldErrors]="fieldErrors()"
      (confirm)="confirmed = confirmed + 1"
      (cancel)="cancelled = cancelled + 1; open.set(false)"
    >
      <dga-text-input class="name" label="name" [(value)]="name" [error]="error() | fieldError: 'userName'" />
      <dga-text-input class="email" label="email" [(value)]="email" />
    </app-editor-dialog-pattern>
  `,
})
class EditorHost {
  readonly open = signal(false);
  readonly busy = signal(false);
  readonly error = signal<ApiError | null>(null);
  readonly translatedCodes = signal<ReadonlySet<string> | undefined>(undefined);
  readonly fieldErrors = signal<FieldErrorMap>({});
  readonly name = signal('');
  readonly email = signal('');
  confirmed = 0;
  cancelled = 0;
}

describe('EditorDialogPatternComponent', () => {
  let fixture: ComponentFixture<EditorHost>;
  let host: HTMLElement;
  let screen: EditorHost;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [EditorHost], providers: patternTestProviders() });
    fixture = TestBed.createComponent(EditorHost);
    document.body.append(fixture.nativeElement as HTMLElement);
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
    screen = fixture.componentInstance;
  });

  const dialog = () => host.querySelector('dialog') as HTMLDialogElement;
  const opener = () => host.querySelector<HTMLButtonElement>('.opener') as HTMLButtonElement;
  const footerButtons = () => [...host.querySelectorAll<HTMLButtonElement>('.editor-dialog__footer dga-button button')];

  function openDialog(): void {
    opener().focus();
    opener().click();
    fixture.detectChanges();
  }

  function key(target: Element, name: string, shiftKey = false): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key: name, shiftKey, bubbles: true, cancelable: true });
    target.dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  it('stays closed until open is set, and is labelled by its title', () => {
    expect(dialog().hasAttribute('open')).toBe(false);
    openDialog();
    expect(dialog().hasAttribute('open')).toBe(true);
    expect(dialog().getAttribute('aria-labelledby')).toBe(host.querySelector('h2')?.id);
    expect(textOf(host, 'h2')).toBe('admin.users.edit-title');
  });

  it('moves focus to the first field on open and back to the opener on close', () => {
    openDialog();
    expect(document.activeElement).toBe(host.querySelector('.name input'));
    screen.open.set(false);
    fixture.detectChanges();
    expect(dialog().hasAttribute('open')).toBe(false);
    expect(document.activeElement).toBe(opener());
  });

  it('keeps Tab and Shift+Tab inside the dialog', () => {
    openDialog();
    const confirm = footerButtons()[1];
    confirm.focus();
    const forward = key(confirm, 'Tab');
    expect(forward.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(host.querySelector('.editor-dialog__close button'));
    const backward = key(document.activeElement as Element, 'Tab', true);
    expect(backward.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(confirm);
  });

  it('emits cancel on Escape, the cancel button and the close button', () => {
    openDialog();
    key(host.querySelector('.name input') as Element, 'Escape');
    expect(screen.cancelled).toBe(1);
    expect(dialog().hasAttribute('open')).toBe(false);

    openDialog();
    footerButtons()[0].click();
    openDialog();
    host.querySelector<HTMLButtonElement>('.editor-dialog__close button')?.click();
    expect(screen.cancelled).toBe(3);
  });

  it('turns the browser cancel event into one cancel output, never a self-close', () => {
    openDialog();
    const event = new Event('cancel', { cancelable: true });
    dialog().dispatchEvent(event);
    expect(event.defaultPrevented).toBe(true);
    expect(screen.cancelled).toBe(1);
  });

  it('emits confirm on submit (Enter or the confirm button)', () => {
    openDialog();
    host.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    expect(screen.confirmed).toBe(1);
  });

  it('emits nothing while busy and shows the confirm as loading', () => {
    openDialog();
    screen.busy.set(true);
    fixture.detectChanges();
    host.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    key(dialog(), 'Escape');
    expect(screen.confirmed).toBe(0);
    expect(screen.cancelled).toBe(0);
    expect(host.querySelector('form')?.getAttribute('aria-busy')).toBe('true');
    expect(footerButtons()[0].disabled).toBe(true);
    expect(footerButtons()[1].disabled).toBe(true);
  });

  it('lists the problem+json field errors with the message and reference code', () => {
    openDialog();
    screen.error.set(parseApiError({
      status: BAD_REQUEST,
      error: {
        code: 'VALIDATION_FAILED',
        correlationId: '5c1e9b0d2f7a4e3c8b6d1a0f9e8d7c6b',
        errors: { userName: ['USER_NAME_REQUIRED'], email: ['EMAIL_INVALID'] },
      },
    }));
    fixture.detectChanges();
    const alert = host.querySelector('[role="alert"]') as HTMLElement;
    expect(textOf(alert, '.editor-dialog__error-message')).toBe('errors.VALIDATION_FAILED');
    expect([...alert.querySelectorAll('li')].map((li) => li.textContent?.trim())).toEqual([
      'errors.USER_NAME_REQUIRED',
      'errors.EMAIL_INVALID',
    ]);
    expect(textOf(alert, '.editor-dialog__error-reference')).toContain('patterns.common.reference-code|code=');
    expect(textOf(host, '.name .dga-field__message--error')).toBe('errors.USER_NAME_REQUIRED');
  });

  it('prefers an explicit field-error map over the error’s own', () => {
    openDialog();
    screen.fieldErrors.set(fieldErrorsFrom(parseApiError({
      status: BAD_REQUEST,
      error: { code: 'VALIDATION_FAILED', errors: { email: ['EMAIL_TAKEN'] } },
    })));
    fixture.detectChanges();
    expect([...host.querySelectorAll('[role="alert"] li')].map((li) => li.textContent?.trim())).toEqual(['errors.EMAIL_TAKEN']);
    expect(host.querySelector('.editor-dialog__error-message')).toBeNull();
  });

  it('shows a module code as itself only when the screen lists it in translatedCodes', () => {
    openDialog();
    screen.error.set(parseApiError({ status: CONFLICT, error: { code: 'IDENTITY_EMAIL_TAKEN' } }));
    fixture.detectChanges();
    expect(textOf(host, '.editor-dialog__error-message')).toBe('errors.INTERNAL_ERROR');
    screen.translatedCodes.set(new Set(['IDENTITY_EMAIL_TAKEN']));
    fixture.detectChanges();
    expect(textOf(host, '.editor-dialog__error-message')).toBe('errors.IDENTITY_EMAIL_TAKEN');
  });

  it('shows no alert without errors', () => {
    openDialog();
    expect(host.querySelector('[role="alert"]')).toBeNull();
  });
});
