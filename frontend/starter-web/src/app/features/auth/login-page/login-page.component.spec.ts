import { computed, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, type Params } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SignInMode } from '../../../core/auth/sign-in-mode';
import { CoreTranslationKeys } from '../../../core/i18n/core-translation-keys';
import { LoginNotices, RouteQueryParams, SessionEndReasons } from '../../../core/routing/route-paths';
import { fakeAuth, layoutTestProviders } from '../../../layouts/layout.testing';
import { LoginFieldMessageKeys, LoginMethodTextKeys, LoginTextKeys } from '../login-form';
import { LoginModesService, LoginModesStates, type LoginModesState } from '../login-modes.service';
import { SignInService, type SignInFailure } from '../sign-in.service';
import { LoginPageComponent } from './login-page.component';

describe('LoginPageComponent', () => {
  const failure = signal<SignInFailure | null>(null);
  const busy = signal(false);
  const signIn = vi.fn();
  const startSso = vi.fn();
  const clearFailure = vi.fn();
  const state = signal<LoginModesState>(LoginModesStates.Ready);
  const methods = signal<readonly SignInMode[]>([SignInMode.Basic]);
  const modesFailure = signal<SignInFailure | null>(null);
  const load = vi.fn();
  const query = new BehaviorSubject(convertToParamMap({}));
  let fixture: ComponentFixture<LoginPageComponent>;
  let host: HTMLElement;

  function render(enabled: readonly SignInMode[] = [SignInMode.Basic]): void {
    methods.set(enabled);
    fixture = TestBed.createComponent(LoginPageComponent);
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    failure.set(null);
    busy.set(false);
    state.set(LoginModesStates.Ready);
    modesFailure.set(null);
    signIn.mockReset().mockResolvedValue(true);
    [startSso, clearFailure, load].forEach(mock => mock.mockReset());
    query.next(convertToParamMap({}));
    TestBed.configureTestingModule({
      imports: [LoginPageComponent],
      providers: [...layoutTestProviders(fakeAuth(null)), { provide: ActivatedRoute, useValue: { queryParamMap: query } }],
    });
    TestBed.overrideComponent(LoginPageComponent, {
      set: {
        providers: [
          { provide: SignInService, useValue: { failure, busy, signIn, startSso, clearFailure } },
          {
            provide: LoginModesService,
            useValue: { state, methods, failure: modesFailure, load, hasChoice: computed(() => methods().length > 1) },
          },
        ],
      },
    });
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

  const tabs = () => [...host.querySelectorAll<HTMLButtonElement>('[role="tab"]')];
  const fieldErrors = () => [...host.querySelectorAll('dga-text-input')].map(field => field.textContent ?? '');

  async function choose(index: number): Promise<void> {
    tabs()[index]?.click();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  describe('the method chooser', () => {
    it('loads the enabled methods when it opens', () => {
      render();
      expect(load).toHaveBeenCalledTimes(1);
    });

    it('renders a single basic method as the form alone, without tabs', () => {
      render([SignInMode.Basic]);
      expect(tabs()).toHaveLength(0);
      expect(host.querySelector('form')).not.toBeNull();
      expect(host.textContent).toContain(LoginMethodTextKeys[SignInMode.Basic].subtitle);
    });

    it('renders a single SSO method as the SSO button alone', () => {
      render([SignInMode.Sso]);
      expect(tabs()).toHaveLength(0);
      expect(host.querySelector('form')).toBeNull();
      expect(host.textContent).toContain('auth.login.sso-submit');
    });

    it('renders a single Active Directory method as the form with the directory subtitle', () => {
      render([SignInMode.ActiveDirectory]);
      expect(tabs()).toHaveLength(0);
      expect(host.textContent).toContain(LoginMethodTextKeys[SignInMode.ActiveDirectory].subtitle);
    });

    it('shows one tab per enabled method, and only those', () => {
      render([SignInMode.Basic, SignInMode.Sso]);
      expect(tabs().map(tab => tab.textContent?.trim())).toEqual([
        LoginMethodTextKeys[SignInMode.Basic].tab,
        LoginMethodTextKeys[SignInMode.Sso].tab,
      ]);
      expect(host.querySelector('[role="tablist"]')?.getAttribute('aria-label')).toBe(LoginTextKeys.MethodsLabel);
      expect(host.querySelector('[role="tabpanel"]')).not.toBeNull();
    });

    it('shows all three methods when all are enabled', () => {
      render([SignInMode.Basic, SignInMode.ActiveDirectory, SignInMode.Sso]);
      expect(tabs()).toHaveLength(3);
    });

    it('submits the Active Directory form after choosing that tab', async () => {
      render([SignInMode.Basic, SignInMode.ActiveDirectory]);
      await choose(1);
      expect(clearFailure).toHaveBeenCalled();
      type(0, 'ad-user');
      type(1, 'secret');
      await submit();
      expect(signIn).toHaveBeenCalledWith({ userName: 'ad-user', password: 'secret' }, SignInMode.ActiveDirectory, null);
    });

    it('starts SSO with the return URL after choosing that tab', async () => {
      render([SignInMode.Basic, SignInMode.Sso]);
      setQuery({ [RouteQueryParams.ReturnUrl]: '/admin' });
      await choose(1);
      expect(host.querySelector('form')).toBeNull();
      (host.querySelector('.login__sso button') as HTMLButtonElement).click();
      expect(startSso).toHaveBeenCalledWith('/admin');
    });

    it('says while it loads the methods', () => {
      state.set(LoginModesStates.Loading);
      render([]);
      expect(host.textContent).toContain(LoginTextKeys.ModesLoading);
      expect(host.querySelector('form')).toBeNull();
    });

    it('offers a retry when the methods could not be loaded', () => {
      state.set(LoginModesStates.Failed);
      modesFailure.set({ messageKey: 'errors.INTERNAL_ERROR', referenceCode: 'ABC-123' });
      render([]);
      expect(host.querySelector('[role="alert"]')?.textContent).toContain(LoginTextKeys.ModesFailed);
      expect(host.textContent).toContain('code=ABC-123');
      load.mockClear();
      (host.querySelector('.login__card dga-button button') as HTMLButtonElement).click();
      expect(load).toHaveBeenCalledTimes(1);
    });

    it('says sign-in is unavailable when no method is enabled', () => {
      render([]);
      expect(host.textContent).toContain(LoginTextKeys.NoMethods);
      expect(host.querySelector('form')).toBeNull();
    });
  });

  describe('the password form', () => {
    beforeEach(() => render());

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

    it('sends the typed credentials with the method and the return URL', async () => {
      setQuery({ [RouteQueryParams.ReturnUrl]: '/admin' });
      type(0, 'admin');
      type(1, 'secret');
      await submit();
      expect(signIn).toHaveBeenCalledWith({ userName: 'admin', password: 'secret' }, SignInMode.Basic, '/admin');
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

    it('confirms an activated account', () => {
      setQuery({ [RouteQueryParams.Notice]: LoginNotices.Activated });
      expect(host.querySelector('.login__notice--success')?.textContent).toBe(LoginTextKeys.ActivatedNotice);
    });

    it('marks the submit button busy while signing in', () => {
      busy.set(true);
      fixture.detectChanges();
      expect(host.querySelector('button[type="submit"]')?.getAttribute('aria-busy')).toBe('true');
    });
  });
});
