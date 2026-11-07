import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { DgaIconComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { PermissionStore } from '../../core/permissions/permission.store';
import { RoutePaths } from '../../core/routing/route-paths';
import { LayoutBrand } from '../app-footer/layout-brand';
import { LanguageSwitcherComponent } from '../language-switcher/language-switcher.component';
import { NotificationsBellComponent } from '../notifications-menu/notifications-bell.component';
import { UserProfileMenuComponent } from '../user-profile-menu/user-profile-menu.component';
import { SHELL_NAV_ITEMS, appLink, visibleNavItems } from './shell-nav';

/**
 * The staff layout: a header with the configured brand (name and logo from `config.json` branding), the permission-gated main navigation, the language switcher
 * and the user menu, and the page below it. Logical CSS only, so the layout mirrors with `<html dir>`.
 */
@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    DgaIconComponent,
    TranslatePipe,
    LanguageSwitcherComponent,
    NotificationsBellComponent,
    UserProfileMenuComponent,
  ],
  templateUrl: './app-shell.component.html',
  styleUrl: './app-shell.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppShellComponent {
  private readonly permissions = inject(PermissionStore);
  private readonly items = inject(SHELL_NAV_ITEMS);

  protected readonly brand = inject(LayoutBrand);

  readonly homeLink = appLink(RoutePaths.Dashboard);
  readonly navItems = computed(() => visibleNavItems(this.items, key => this.permissions.has(key)));
}
