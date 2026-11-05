import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DgaButtonComponent, DgaCardComponent } from '@codelab/ng-dga';
import { AuthService } from '../../../core/auth/auth.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { RoutePaths } from '../../../core/routing/route-paths';
import { appLink } from '../../../layouts/app-shell/shell-nav';

/**
 * The 403 page, where `permissionGuard` and the forbidden interceptor send a user who lacks a key. A
 * signed-in user can go home or sign in as someone else (logout, then the login page); a signed-out
 * visitor gets the login link.
 */
@Component({
  selector: 'app-unauthorized-page',
  imports: [DgaButtonComponent, DgaCardComponent, RouterLink, TranslatePipe],
  templateUrl: './unauthorized-page.component.html',
  styleUrl: '../error-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UnauthorizedPageComponent {
  private readonly auth = inject(AuthService);

  readonly isAuthenticated = this.auth.isAuthenticated;
  readonly homeLink = appLink(RoutePaths.Dashboard);
  readonly loginLink = appLink(RoutePaths.Login);

  signInAsAnotherUser(): Promise<void> {
    return this.auth.logout();
  }
}
