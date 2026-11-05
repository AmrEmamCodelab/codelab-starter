import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DgaCardComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { PermissionStore } from '../../../core/permissions/permission.store';
import { HasPermissionDirective } from '../../../shared/permissions/has-permission.directive';
import { ADMIN_TILES, visibleTiles } from './admin-tiles';

/**
 * The admin home (`/admin`, `access.admin.view`): a tile per admin area (users, organizations, import, roles), each
 * shown only to a holder of the key of the page it opens. Grants and revokes repaint it live (`PermissionStore`).
 */
@Component({
  selector: 'app-admin-page',
  imports: [DgaCardComponent, HasPermissionDirective, RouterLink, TranslatePipe],
  templateUrl: './admin-page.component.html',
  styleUrl: './admin-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminPageComponent {
  private readonly permissions = inject(PermissionStore);
  protected readonly tiles = ADMIN_TILES;
  protected readonly hasAnyTile = computed(() => visibleTiles((key) => this.permissions.has(key)).length > 0);
}
