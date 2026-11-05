import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { parseApiError, type ApiError } from '@codelab/ng-security';
import { beforeEach, describe, expect, it } from 'vitest';
import { patternTestProviders, textOf } from '../patterns.testing';
import { DecisionDialogComponent } from './decision-dialog.component';
import { DecisionDialogTranslationKeys } from './decision-dialog-translation-keys';

const BAD_REQUEST = 400;
const VALID_REASON = 'Documents are missing';

/** Living documentation: a screen asking for the reason of a "reject" decision. */
@Component({
  imports: [DecisionDialogComponent],
  template: `
    <div [attr.dir]="dir()">
      <button type="button" class="reject" data-public-action (click)="open.set(true)">reject</button>
      <app-decision-dialog
        titleKey="cases.reject.title"
        messageKey="cases.reject.message"
        [messageParams]="{ reference: 'C-7' }"
        confirmKey="cases.actions.reject"
        destructive
        [open]="open()"
        [busy]="busy()"
        [error]="error()"
        [(reason)]="reason"
        (confirm)="confirmed.push($event)"
        (cancel)="open.set(false)"
      />
    </div>
  `,
})
class RejectHost {
  readonly dir = signal<'rtl' | 'ltr'>('rtl');
  readonly open = signal(false);
  readonly busy = signal(false);
  readonly error = signal<ApiError | null>(null);
  readonly reason = signal('');
  readonly confirmed: string[] = [];
}

describe('DecisionDialogComponent', () => {
  let fixture: ComponentFixture<RejectHost>;
  let host: HTMLElement;
  let screen: RejectHost;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [RejectHost], providers: patternTestProviders() });
    fixture = TestBed.createComponent(RejectHost);
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
    screen = fixture.componentInstance;
  });

  const render = () => fixture.detectChanges();
  const textarea = () => host.querySelector('textarea') as HTMLTextAreaElement;
  const form = () => host.querySelector('form') as HTMLFormElement;
  const submit = () => {
    form().dispatchEvent(new Event('submit', { cancelable: true }));
    render();
  };
  const type = (text: string) => {
    textarea().value = text;
    textarea().dispatchEvent(new Event('input'));
    render();
  };

  function openDialog(): void {
    const opener = host.querySelector('.reject') as HTMLButtonElement;
    opener.focus();
    opener.click();
    render();
  }

  it('opens with the title, the message, a labelled required reason and focus in the field', () => {
    openDialog();
    expect(textOf(host, 'h2')).toBe('cases.reject.title');
    expect(textOf(host, '.decision-dialog__message')).toBe('cases.reject.message|reference=C-7');
    const label = host.querySelector('label') as HTMLLabelElement;
    expect(label.htmlFor).toBe(textarea().id);
    expect(label.textContent?.trim()).toBe(DecisionDialogTranslationKeys.Reason);
    expect(textarea().required).toBe(true);
    expect(textarea().getAttribute('maxlength')).toBe('500');
    expect(document.activeElement).toBe(textarea());
  });

  it('shows the counter and links it to the field', () => {
    openDialog();
    type('abc');
    const counter = host.querySelector('.decision-dialog__counter') as HTMLElement;
    expect(counter.textContent?.trim()).toBe(`${DecisionDialogTranslationKeys.Counter}|count=3,max=500`);
    expect(textarea().getAttribute('aria-describedby')).toBe(counter.id);
  });

  it('refuses an empty reason on confirm: no event, a message, aria-invalid and focus back in the field', () => {
    openDialog();
    (host.querySelector('.reject') as HTMLButtonElement).focus();
    submit();
    expect(screen.confirmed).toEqual([]);
    expect(textOf(host, '.decision-dialog__error')).toBe(DecisionDialogTranslationKeys.ReasonRequired);
    expect(textarea().getAttribute('aria-invalid')).toBe('true');
    expect(textarea().getAttribute('aria-describedby')).toContain(host.querySelector('.decision-dialog__error')?.id);
    expect(document.activeElement).toBe(textarea());
  });

  it('does not nag while the user starts typing, and shows a short reason after the field is left', () => {
    openDialog();
    type('ab');
    expect(host.querySelector('.decision-dialog__error')).toBeNull();
    textarea().dispatchEvent(new Event('blur'));
    render();
    expect(textOf(host, '.decision-dialog__error')).toBe(`${DecisionDialogTranslationKeys.ReasonTooShort}|min=5`);
  });

  it('emits the trimmed reason when it is valid', () => {
    openDialog();
    type(`  ${VALID_REASON}  `);
    submit();
    expect(screen.confirmed).toEqual([VALID_REASON]);
    expect(screen.reason()).toBe(`  ${VALID_REASON}  `);
  });

  it('confirms through the submit button labelled with the action (Enter in the textarea adds a line)', () => {
    openDialog();
    type(VALID_REASON);
    const confirm = [...host.querySelectorAll<HTMLButtonElement>('footer button')][1];
    expect(confirm.type).toBe('submit');
    expect(confirm.textContent?.trim()).toBe('cases.actions.reject');
  });

  it('cancels with Escape and closes when the screen says so', () => {
    openDialog();
    host.querySelector('dialog')?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    render();
    expect(screen.open()).toBe(false);
    expect(host.querySelector('dialog')?.hasAttribute('open')).toBe(false);
  });

  it('forgets the shown problems when it opens again', () => {
    openDialog();
    submit();
    expect(host.querySelector('.decision-dialog__error')).not.toBeNull();
    screen.open.set(false);
    render();
    openDialog();
    expect(host.querySelector('.decision-dialog__error')).toBeNull();
  });

  it('shows the server field error under the field and the error alert above it', () => {
    openDialog();
    type(VALID_REASON);
    screen.error.set(parseApiError({
      status: BAD_REQUEST,
      error: { code: 'VALIDATION_FAILED', errors: { reason: ['DECISION_REASON_INVALID'] } },
    }));
    render();
    expect(textOf(host, '.decision-dialog__error')).toBe('errors.DECISION_REASON_INVALID');
    expect(host.querySelector('[role="alert"]')).not.toBeNull();
  });

  it('locks the field and the buttons while the decision is in progress', () => {
    openDialog();
    type(VALID_REASON);
    screen.busy.set(true);
    render();
    expect(textarea().disabled).toBe(true);
    submit();
    expect(screen.confirmed).toEqual([]);
  });

  it('renders the same field in LTR', () => {
    screen.dir.set('ltr');
    openDialog();
    expect(textarea()).not.toBeNull();
    expect(document.activeElement).toBe(textarea());
  });
});
