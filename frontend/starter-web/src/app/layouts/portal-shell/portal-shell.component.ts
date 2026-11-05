import { ChangeDetectionStrategy, Component, computed, ElementRef, inject, viewChild } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { DgaHeaderComponent, DgaIconComponent, DgaStampStripComponent } from '@codelab/ng-dga';
import { AuthService } from '../../core/auth/auth.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationService } from '../../core/i18n/translation.service';
import { PortalPaths, RoutePaths } from '../../core/routing/route-paths';
import { AppFooterComponent } from '../app-footer/app-footer.component';
import { LayoutBrand } from '../app-footer/layout-brand';
import { LanguageSwitcherComponent } from '../language-switcher/language-switcher.component';
import { UserProfileMenuComponent } from '../user-profile-menu/user-profile-menu.component';
import { LayoutChromeTranslationKeys } from './layout-chrome-translation-keys';
import { PORTAL_NAV_ENTRIES, portalLink, rootLink, toDgaNavItems } from './portal-nav';

/** The id of the page region the skip link jumps to. */
export const PORTAL_MAIN_ID = 'portal-main';

/**
 * The public frame of the portal pages: the digital stamp strip, the DGA header (brand, the portal navigation, the
 * language switcher, and a sign-in link for a visitor or the user menu once signed in), the page outlet and the
 * footer. Every portal page renders inside it; it holds no page content of its own and needs no session.
 *
 * Mounted by the app around the portal pages (`app.routes.ts`):
 * `{ path: RoutePaths.Portal, component: PortalShellComponent, children: PORTAL_ROUTES }`, with the portal paths in
 * `PUBLIC_ROUTE_PATHS` (the pages are public, so the route gate checks them as such).
 */
@Component({
  selector: 'app-portal-shell',
  imports: [
    RouterOutlet,
    RouterLink,
    DgaHeaderComponent,
    DgaIconComponent,
    DgaStampStripComponent,
    TranslatePipe,
    AppFooterComponent,
    LanguageSwitcherComponent,
    UserProfileMenuComponent,
  ],
  templateUrl: './portal-shell.component.html',
  styleUrl: './portal-shell.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PortalShellComponent {
  private readonly translations = inject(TranslationService);
  private readonly main = viewChild.required<ElementRef<HTMLElement>>('main');

  protected readonly brand = inject(LayoutBrand);
  protected readonly keys = LayoutChromeTranslationKeys;
  protected readonly mainId = PORTAL_MAIN_ID;
  readonly isAuthenticated = inject(AuthService).isAuthenticated;
  readonly homeLink = portalLink(PortalPaths.Landing);
  readonly signInLink = rootLink(RoutePaths.Login);
  readonly navItems = computed(() => toDgaNavItems(PORTAL_NAV_ENTRIES, key => this.translations.translate(key)));

  /** The skip link moves keyboard focus past the header onto the page. */
  focusMain(): void {
    this.main().nativeElement.focus();
  }
}
