import { computed, inject, signal, type Provider } from '@angular/core';
import type { SessionUser } from '../../core/api/clients/api.models';
import { AuthService } from '../../core/auth/auth.service';
import { TranslationService } from '../../core/i18n/translation.service';
import { REQUESTER_SHELL_OPTIONS, type RequesterShellOptions } from '../../layouts/requester-shell/requester-nav';
import { ShowcasePageId, showcaseLink } from '../showcase-pages';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';

const L = ShowcaseTranslationKeys.Layouts;

/** The sample user's id in the framed requester shell (no real session exists on the showcase). */
export const SAMPLE_USER_ID = 'showcase-user';

/**
 * The part of {@link AuthService} the framed shells read, for a signed-in sample user: the user menu shows the
 * translated sample name, and sign-out does nothing. It is provided only inside the requester-shell showcase page, so
 * the real session of the app is never touched.
 */
export class ShowcaseSession {
  readonly user = computed<SessionUser | null>(() => ({ id: SAMPLE_USER_ID, displayName: this.translate(L.SampleUser) }));
  readonly isAuthenticated = signal(true).asReadonly();

  constructor(private readonly translate: (key: string) => string) {}

  logout(): Promise<void> {
    return Promise.resolve();
  }
}

/** The framed requester shell's navigation: a submenu and a plain entry, all pointing at showcase pages. */
export const SHOWCASE_REQUESTER_OPTIONS: RequesterShellOptions = {
  homeLink: showcaseLink(ShowcasePageId.RequesterShell),
  notificationsLink: showcaseLink(ShowcasePageId.Layouts),
  navItems: [
    {
      id: 'requests',
      labelKey: L.NavRequests,
      children: [
        { id: 'new-request', labelKey: L.NavNewRequest, link: showcaseLink(ShowcasePageId.Wizard) },
        { id: 'my-requests', labelKey: L.NavMyRequests, link: showcaseLink(ShowcasePageId.RequesterDetail) },
      ],
    },
    { id: 'help', labelKey: L.NavHelp, link: showcaseLink(ShowcasePageId.PortalContent) },
  ],
};

/** The providers of the framed requester shell: the sample session and the showcase navigation. */
export function provideShowcaseRequesterFrame(): Provider[] {
  return [
    {
      provide: AuthService,
      useFactory: () => {
        const translations = inject(TranslationService);
        return new ShowcaseSession(key => translations.translate(key));
      },
    },
    { provide: REQUESTER_SHELL_OPTIONS, useValue: SHOWCASE_REQUESTER_OPTIONS },
  ];
}
