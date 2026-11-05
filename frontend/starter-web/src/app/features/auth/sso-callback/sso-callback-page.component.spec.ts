import { signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { LoginRejection } from '../../../core/auth/login-rejection';
import { fakeAuth, layoutTestProviders } from '../../../layouts/layout.testing';
import { SsoCallbackPageComponent, SsoCallbackTextKeys } from './sso-callback-page.component';
import { SsoCallbackService, SsoCallbackStates, type SsoCallbackState } from './sso-callback.service';

describe('SsoCallbackPageComponent', () => {
  const state = signal<SsoCallbackState>(SsoCallbackStates.Working);
  const failure = signal<LoginRejection | null>(null);
  const complete = vi.fn();
  const backToLogin = vi.fn();
  let fixture: ComponentFixture<SsoCallbackPageComponent>;
  let host: HTMLElement;

  beforeEach(() => {
    state.set(SsoCallbackStates.Working);
    failure.set(null);
    complete.mockReset().mockResolvedValue(undefined);
    backToLogin.mockReset().mockResolvedValue(undefined);
    TestBed.configureTestingModule({ imports: [SsoCallbackPageComponent], providers: layoutTestProviders(fakeAuth(null)) });
    TestBed.overrideComponent(SsoCallbackPageComponent, {
      set: { providers: [{ provide: SsoCallbackService, useValue: { state, failure, complete, backToLogin } }] },
    });
    fixture = TestBed.createComponent(SsoCallbackPageComponent);
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  });

  it('runs the callback once when it opens and shows progress', () => {
    expect(complete).toHaveBeenCalledTimes(1);
    expect(host.querySelector('[role="status"]')?.textContent).toContain(SsoCallbackTextKeys.Completing);
  });

  it('shows the refusal and a way back to the login page', () => {
    state.set(SsoCallbackStates.Failed);
    failure.set({ messageKey: 'errors.IDENTITY_SSO_FAILED', referenceCode: null });
    fixture.detectChanges();
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('errors.IDENTITY_SSO_FAILED');
    (host.querySelector('dga-button button') as HTMLButtonElement).click();
    expect(backToLogin).toHaveBeenCalled();
  });
});
