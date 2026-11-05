import { signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, type Params } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CoreTranslationKeys } from '../../../core/i18n/core-translation-keys';
import { RouteQueryParams, SessionEndReasons } from '../../../core/routing/route-paths';
import { fakeAuth, layoutTestProviders } from '../../../layouts/layout.testing';
import { LoginFieldMessageKeys } from '../login-form';
import { SignInService, type SignInFailure } from '../sign-in.service';
import { LoginPageComponent } from './login-page.component';

describe('LoginPageComponent', () => {
  const failure = signal<SignInFailure | null>(null);
  const busy = signal(false);
  const signIn = vi.fn();
  const query = new BehaviorSubject(convertToParamMap({}));
  let fixture: ComponentFixture<LoginPageComponent>;
  let host: HTMLElement;

  beforeEach(() => {
    failure.set(null);
    busy.set(false);
    signIn.mockReset().mockResolvedValue(true);
    query.next(convertToParamMap({}));
    TestBed.configureTestingModule({
      imports: [LoginPageComponent],
      providers: [...layoutTestProviders(fakeAuth(null)), { provide: ActivatedRoute, useValue: { queryParamMap: query } }],
    });
    TestBed.overrideComponent(LoginPageComponent, {
      set: { providers: [{ provide: SignInService, useValue: { failure, busy, signIn } }] },
    });
    fixture = TestBed.createComponent(LoginPageComponent);
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  });

  function setQuery(params: Params): void {
    query.next(convertToParamMap(params));
    fixture.detectChanges();
  }

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

  it('shows no field errors before the first submit', () => {
    expect(fieldErrors().join('')).not.toContain(LoginFieldMessageKeys.UserNameRequired);
  });

  it('does not send an empty form, and shows both field errors', async () => {
    await submit();
    expect(signIn).not.toHaveBeenCalled();
    const [userName, password] = fieldErrors();
    expect(userName).toContain(LoginFieldMessageKeys.UserNameRequired);
    expect(password).toContain(LoginFieldMessageKeys.PasswordRequired);
  });

  it('sends the typed credentials with the return URL', async () => {
    setQuery({ [RouteQueryParams.ReturnUrl]: '/admin' });
    type(0, 'admin');
    type(1, 'secret');
    await submit();
    expect(signIn).toHaveBeenCalledWith({ userName: 'admin', password: 'secret' }, '/admin');
  });

  it('shows a refusal inline with its reference code', () => {
    failure.set({ messageKey: 'errors.INTERNAL_ERROR', referenceCode: 'ABC-123' });
    fixture.detectChanges();
    const alert = host.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('errors.INTERNAL_ERROR');
    expect(alert?.textContent).toContain('core.error-dialog.reference-code|code=ABC-123');
  });

  it('explains why the previous session ended', () => {
    setQuery({ [RouteQueryParams.Reason]: SessionEndReasons.Idle });
    expect(host.querySelector('[role="status"]')?.textContent).toBe(CoreTranslationKeys.SessionEnded.idle);
  });

  it('marks the submit button busy while signing in', () => {
    busy.set(true);
    fixture.detectChanges();
    expect(host.querySelector('button[type="submit"]')?.getAttribute('aria-busy')).toBe('true');
  });
});
