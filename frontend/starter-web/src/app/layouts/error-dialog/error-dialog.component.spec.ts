import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { DGA_CLIPBOARD } from '@codelab/ng-dga';
import { parseApiError } from '@codelab/ng-security';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ErrorDialogStore } from '../../core/http/error-dialog.store';
import { fakeAuth, layoutTestProviders } from '../layout.testing';
import { ErrorDialogTranslationKeys } from './error-dialog-translation-keys';
import { ErrorDialogComponent } from './error-dialog.component';

const SERVER_ERROR = 500;

describe('ErrorDialogComponent', () => {
  let store: ErrorDialogStore;
  let fixture: ComponentFixture<ErrorDialogComponent>;
  let host: HTMLElement;
  let clipboard: { write: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    clipboard = { write: vi.fn().mockResolvedValue(true) };
    TestBed.configureTestingModule({
      imports: [ErrorDialogComponent],
      providers: [...layoutTestProviders(fakeAuth()), { provide: DGA_CLIPBOARD, useValue: clipboard }],
    });
    store = TestBed.inject(ErrorDialogStore);
    fixture = TestBed.createComponent(ErrorDialogComponent);
    host = fixture.nativeElement as HTMLElement;
    document.body.appendChild(host);
    fixture.detectChanges();
  });

  afterEach(() => document.body.replaceChildren());

  async function showServerError(): Promise<void> {
    store.show(parseApiError({ status: SERVER_ERROR, error: { code: 'INTERNAL_ERROR', correlationId: '5c1e9b0d2f7a4e3c8b6d1a0f9e8d7c6b' } }));
    fixture.detectChanges();
    await fixture.whenStable();
  }

  const dialog = () => host.querySelector('[role="alertdialog"]') as HTMLDialogElement | null;

  it('renders nothing without an error', () => {
    expect(dialog()).toBeNull();
  });

  it('opens the DGA error dialog with the message for the code and the reference code', async () => {
    await showServerError();
    expect(dialog()?.hasAttribute('open')).toBe(true);
    expect(dialog()?.querySelector('h2')?.textContent).toBe(ErrorDialogTranslationKeys.Title);
    expect(dialog()?.getAttribute('aria-labelledby')).toBe(dialog()?.querySelector('h2')?.id);
    expect(dialog()?.querySelector('.dga-error-dialog__message')?.textContent).toBe('errors.INTERNAL_ERROR');
    expect(dialog()?.querySelector('.dga-error-dialog__reference-label')?.textContent).toBe(ErrorDialogTranslationKeys.Reference);
    expect(store.referenceCode()).not.toBeNull();
    expect(dialog()?.querySelector('.dga-error-dialog__reference-code')?.textContent).toBe(store.referenceCode());
  });

  it('closes on the close button', async () => {
    await showServerError();
    const close = host.querySelector('.dga-error-dialog__close button') as HTMLButtonElement;
    expect(close.textContent).toContain(ErrorDialogTranslationKeys.Close);
    close.click();
    fixture.detectChanges();
    expect(store.current()).toBeNull();
    expect(dialog()).toBeNull();
  });

  it('closes on Escape', async () => {
    await showServerError();
    dialog()?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    expect(store.current()).toBeNull();
  });

  it('copies the reference code', async () => {
    await showServerError();
    const copy = host.querySelector('.dga-error-dialog__copy button') as HTMLButtonElement;
    expect(copy.textContent).toContain(ErrorDialogTranslationKeys.Copy);
    copy.click();
    expect(clipboard.write).toHaveBeenCalledWith(store.referenceCode());
  });
});
