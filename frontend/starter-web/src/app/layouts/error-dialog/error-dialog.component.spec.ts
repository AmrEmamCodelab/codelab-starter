import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { parseApiError } from '@codelab/ng-security';
import { beforeEach, describe, expect, it } from 'vitest';
import { ErrorDialogStore } from '../../core/http/error-dialog.store';
import { fakeAuth, layoutTestProviders } from '../layout.testing';
import { ErrorDialogComponent } from './error-dialog.component';

const SERVER_ERROR = 500;

describe('ErrorDialogComponent', () => {
  let store: ErrorDialogStore;
  let fixture: ComponentFixture<ErrorDialogComponent>;
  let host: HTMLElement;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ErrorDialogComponent],
      providers: layoutTestProviders(fakeAuth()),
    });
    store = TestBed.inject(ErrorDialogStore);
    fixture = TestBed.createComponent(ErrorDialogComponent);
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  });

  function showServerError(): void {
    store.show(parseApiError({ status: SERVER_ERROR, error: { code: 'INTERNAL_ERROR', correlationId: '5c1e9b0d2f7a4e3c8b6d1a0f9e8d7c6b' } }));
    fixture.detectChanges();
  }

  it('renders nothing without an error', () => {
    expect(host.querySelector('[role="alertdialog"]')).toBeNull();
  });

  it('shows the message for the code and the reference code', () => {
    showServerError();
    const dialog = host.querySelector('[role="alertdialog"]');
    expect(dialog?.querySelector('.error-dialog__message')?.textContent).toBe('errors.INTERNAL_ERROR');
    expect(dialog?.querySelector('.error-dialog__reference')?.textContent).toBe(
      `core.error-dialog.reference-code|code=${store.referenceCode()}`,
    );
    expect(dialog?.getAttribute('aria-labelledby')).toBe(dialog?.querySelector('h2')?.id);
  });

  it('closes on the dismiss button', () => {
    showServerError();
    (host.querySelector('dga-button button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(store.current()).toBeNull();
    expect(host.querySelector('[role="alertdialog"]')).toBeNull();
  });

  it('closes on Escape', () => {
    showServerError();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(store.current()).toBeNull();
  });
});
