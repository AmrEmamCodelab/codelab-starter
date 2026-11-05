import { ChangeDetectionStrategy, Component, computed, ElementRef, inject, viewChild } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { DgaHeaderComponent, DgaStampStripComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationService } from '../../core/i18n/translation.service';
import { PermissionStore } from '../../core/permissions/permission.store';
import { AppFooterComponent } from '../app-footer/app-footer.component';
import { LayoutBrand } from '../app-footer/layout-brand';
import { LanguageSwitcherComponent } from '../language-switcher/language-switcher.component';
import { NotificationsMenuComponent } from '../notifications-menu/notifications-menu.component';
import { UserProfileMenuComponent } from '../user-profile-menu/user-profile-menu.component';
import { NOTIFICATIONS_SOURCE } from '../notifications-menu/notifications';
import { LayoutChromeTranslationKeys } from '../portal-shell/layout-chrome-translation-keys';
import { REQUESTER_SHELL_OPTIONS, visibleRequesterNav } from './requester-nav';

/** The id of the page region the skip link jumps to. */
export const REQUESTER_MAIN_ID = 'requester-main';

/**
 * The signed-in requester's frame: the digital stamp strip, the DGA header (brand, the permission-filtered requester
 * navigation, which collapses behind a menu button on phones, the notifications bell, the language switcher and the
 * user menu), the page outlet and the footer.
 *
 * The navigation and the notifications link come from `REQUESTER_SHELL_OPTIONS`, the notifications from
 * `NOTIFICATIONS_SOURCE`; the app provides both where it mounts the shell (P7: a layout route behind `authGuard` whose
 * children carry their own permission keys, listed in `LAYOUT_ROUTES`). Not routed in P4.
 */
@Component({
  selector: 'app-requester-shell',
  imports: [
    RouterOutlet,
    RouterLink,
    DgaHeaderComponent,
    DgaStampStripComponent,
    TranslatePipe,
    AppFooterComponent,
    LanguageSwitcherComponent,
    NotificationsMenuComponent,
    UserProfileMenuComponent,
  ],
  templateUrl: './requester-shell.component.html',
  styleUrl: './requester-shell.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RequesterShellComponent {
  private readonly translations = inject(TranslationService);
  private readonly permissions = inject(PermissionStore);
  private readonly options = inject(REQUESTER_SHELL_OPTIONS);
  private readonly main = viewChild.required<ElementRef<HTMLElement>>('main');

  protected readonly brand = inject(LayoutBrand);
  protected readonly keys = LayoutChromeTranslationKeys;
  protected readonly mainId = REQUESTER_MAIN_ID;
  readonly notifications = inject(NOTIFICATIONS_SOURCE);
  readonly homeLink = this.options.homeLink;
  readonly notificationsLink = this.options.notificationsLink;
  readonly navItems = computed(() =>
    visibleRequesterNav(
      this.options.navItems,
      key => this.permissions.has(key),
      key => this.translations.translate(key),
    ),
  );

  markRead(id: string): Promise<void> {
    return this.notifications.markRead(id);
  }

  /** The skip link moves keyboard focus past the header onto the page. */
  focusMain(): void {
    this.main().nativeElement.focus();
  }
}
