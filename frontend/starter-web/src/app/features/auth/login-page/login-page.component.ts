import { ChangeDetectionStrategy, Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import {
  DgaButtonComponent,
  DgaCardComponent,
  DgaTabsComponent,
  DgaTextInputComponent,
  dgaTabPanelId,
  type DgaTab,
  type DgaTabValue,
} from '@codelab/ng-dga';
import { SignInMode } from '../../../core/auth/sign-in-mode';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import { RouteQueryParams } from '../../../core/routing/route-paths';
import { LanguageSwitcherComponent } from '../../../layouts/language-switcher/language-switcher.component';
import {
  LoginMethodTextKeys,
  LoginTextKeys,
  credentialErrors,
  hasCredentialErrors,
  loginNoticeKey,
  sessionEndedNoticeKey,
  usesPasswordForm,
  type Credentials,
} from '../login-form';
import { LoginModesService, LoginModesStates } from '../login-modes.service';
import { SignInService } from '../sign-in.service';

/** The id prefix linking the method tabs to the panel below them. */
export const LOGIN_METHOD_PANEL_PREFIX = 'login-method';

/**
 * The sign-in page: a chooser over the methods the deployment enables (`GET api/auth/modes`), shown as tabs only
 * when there is more than one. Basic and Active Directory share the user name and password form; SSO is a button
 * that leaves for the identity provider. Field checks show after the first submit; a refusal shows inline.
 * `?returnUrl=`, `?reason=` and `?notice=` come from the query string.
 */
@Component({
  selector: 'app-login-page',
  imports: [
    DgaButtonComponent,
    DgaCardComponent,
    DgaTabsComponent,
    DgaTextInputComponent,
    LanguageSwitcherComponent,
    TranslatePipe,
  ],
  providers: [SignInService, LoginModesService],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPageComponent {
  private readonly translations = inject(TranslationService);
  readonly signIn = inject(SignInService);
  readonly modes = inject(LoginModesService);
  readonly states = LoginModesStates;
  readonly texts = LoginTextKeys;
  readonly panelPrefix = LOGIN_METHOD_PANEL_PREFIX;

  private readonly query = toSignal(inject(ActivatedRoute).queryParamMap);

  /** Where to go after signing in; only an app-relative URL is honoured. */
  readonly returnUrl = computed(() => this.query()?.get(RouteQueryParams.ReturnUrl) ?? null);
  readonly noticeKey = computed(() => sessionEndedNoticeKey(this.query()?.get(RouteQueryParams.Reason)));
  readonly successKey = computed(() => loginNoticeKey(this.query()?.get(RouteQueryParams.Notice)));

  /** The selected tab; follows the first enabled method until the user picks another. */
  readonly selectedTab = linkedSignal<DgaTabValue>(() => this.modes.methods()[0] ?? SignInMode.Basic);
  /** The method in use, or null while none is enabled. */
  readonly method = computed(() => {
    const methods = this.modes.methods();
    return methods.find(method => method === this.selectedTab()) ?? methods[0] ?? null;
  });
  readonly tabs = computed<readonly DgaTab[]>(() =>
    this.modes.methods().map(method => ({
      value: method,
      label: this.translations.translate(LoginMethodTextKeys[method].tab),
    })),
  );
  readonly panelId = computed(() => dgaTabPanelId(LOGIN_METHOD_PANEL_PREFIX, this.method() ?? SignInMode.Basic));
  readonly subtitleKey = computed(() => LoginMethodTextKeys[this.method() ?? SignInMode.Basic].subtitle);
  readonly showsPasswordForm = computed(() => {
    const method = this.method();
    return method !== null && usesPasswordForm(method);
  });
  readonly showsSso = computed(() => this.method() === SignInMode.Sso);

  readonly userNameInput = signal('');
  readonly passwordInput = signal('');
  readonly submitted = signal(false);

  private readonly errors = computed(() =>
    credentialErrors({ userName: this.userNameInput(), password: this.passwordInput() }),
  );
  readonly userNameError = computed(() => this.shownError(this.errors().userName));
  readonly passwordError = computed(() => this.shownError(this.errors().password));

  constructor() {
    void this.modes.load();
  }

  selectMethod(value: DgaTabValue): void {
    this.selectedTab.set(value);
    this.submitted.set(false);
    this.signIn.clearFailure();
  }

  retryModes(): void {
    void this.modes.load();
  }

  async submit(event: Event): Promise<void> {
    event.preventDefault();
    const method = this.method();
    this.submitted.set(true);
    if (method === null || hasCredentialErrors(this.errors())) {
      return;
    }
    const credentials: Credentials = { userName: this.userNameInput(), password: this.passwordInput() };
    await this.signIn.signIn(credentials, method, this.returnUrl());
  }

  startSso(): void {
    void this.signIn.startSso(this.returnUrl());
  }

  private shownError(key: string | null): string | null {
    return this.submitted() && key !== null ? this.translations.translate(key) : null;
  }
}
