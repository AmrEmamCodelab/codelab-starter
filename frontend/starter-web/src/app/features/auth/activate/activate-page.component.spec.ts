import { signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { LoginRejection } from '../../../core/auth/login-rejection';
import { RoutePaths } from '../../../core/routing/route-paths';
import { fakeAuth, layoutTestProviders } from '../../../layouts/layout.testing';
import { ActivatePageComponent } from './activate-page.component';
import { ACTIVATION_PASSWORD_MIN_LENGTH, ActivationMessageKeys } from './activation-form';
import { ActivationService } from './activation.service';

describe('ActivatePageComponent', () => {
  const hasTicket = signal(true);
  const busy = signal(false);
  const failure = signal<LoginRejection | null>(null);
  const passwordError = signal<string | null>(null);
  const init = vi.fn();
  const activate = vi.fn();
  let fixture: ComponentFixture<ActivatePageComponent>;
  let host: HTMLElement;

  function render(): void {
    fixture = TestBed.createComponent(ActivatePageComponent);
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    hasTicket.set(true);
    busy.set(false);
    failure.set(null);
    passwordError.set(null);
    init.mockReset();
    activate.mockReset().mockResolvedValue(true);
    TestBed.configureTestingModule({
      imports: [ActivatePageComponent],
      providers: [provideRouter([]), ...layoutTestProviders(fakeAuth(null))],
    });
    TestBed.overrideComponent(ActivatePageComponent, {
      set: {
        providers: [
          { provide: ActivationService, useValue: { hasTicket, busy, failure, passwordError, init, activate } },
        ],
      },
    });
  });

  function type(index: number, value: string): void {
    const input = host.querySelectorAll('input')[index] as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  async function submit(): Promise<void> {
    (host.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit', { cancelable: true }));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const fieldErrors = () => [...host.querySelectorAll('dga-text-input')].map(field => field.textContent ?? '');

  it('reads the ticket once when it opens', () => {
    render();
    expect(init).toHaveBeenCalledTimes(1);
  });

  it('checks the fields before sending', async () => {
    render();
    type(0, 'short');
    type(1, 'other');
    await submit();
    expect(activate).not.toHaveBeenCalled();
    const [password, confirm] = fieldErrors();
    expect(password).toContain(`${ActivationMessageKeys.PasswordTooShort}|min=${ACTIVATION_PASSWORD_MIN_LENGTH}`);
    expect(confirm).toContain(ActivationMessageKeys.ConfirmMismatch);
  });

  it('sends a valid, confirmed password', async () => {
    render();
    type(0, 'a-good-password');
    type(1, 'a-good-password');
    await submit();
    expect(activate).toHaveBeenCalledWith('a-good-password');
  });

  it('shows the server password error under the field', () => {
    render();
    passwordError.set(ActivationMessageKeys.PasswordTooSimple);
    fixture.detectChanges();
    expect(fieldErrors()[0]).toContain(ActivationMessageKeys.PasswordTooSimple);
  });

  it('shows a refused link inline', () => {
    render();
    failure.set({ messageKey: 'errors.IDENTITY_ACTIVATION_REJECTED', referenceCode: null });
    fixture.detectChanges();
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('errors.IDENTITY_ACTIVATION_REJECTED');
  });

  it('explains a link without a ticket and offers the login page', () => {
    hasTicket.set(false);
    render();
    expect(host.querySelector('form')).toBeNull();
    expect(host.querySelector('[role="alert"]')?.textContent).toContain(ActivationMessageKeys.MissingTicket);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    (host.querySelector('.activate__card dga-button button') as HTMLButtonElement).click();
    expect(navigate).toHaveBeenCalledWith([RoutePaths.Login]);
  });
});
