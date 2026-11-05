import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { DgaButtonComponent, DgaStatusTagComponent, DgaTagComponent, DgaTextInputComponent } from '@codelab/ng-dga';
import { LanguageService } from '../../../../core/i18n/language.service';
import { TranslatePipe } from '../../../../core/i18n/translate.pipe';
import { PermissionKeys } from '../../../../core/permissions/permission-keys.generated';
import {
  AdminListPatternComponent,
  EditorDialogPatternComponent,
  FieldErrorPipe,
  ListCellDirective,
  ListRowActionsDirective,
  type ColumnDef,
} from '../../../../patterns';
import { HasPermissionDirective } from '../../../../shared/permissions/has-permission.directive';
import { AccessPaths } from '../access.paths';
import { AccessWarningsComponent, LastManagerDialogComponent } from '../common/access-dialogs.component';
import { errorMessageKey } from '../common/access-errors';
import { localizedName, virtualKindLabelKey } from '../common/access-labels';
import type { RoleSummary } from '../data/access.models';
import { permissionCountOf } from './role-list';
import { RoleListStore } from './role-list.store';

/**
 * The roles list (`/admin/access/roles`, `access.roles.view`) on the admin-list pattern: name in the current
 * language, code, automatic (virtual) or assigned, active state and permission count. Holders of
 * `access.roles.manage` create roles (then choose their permissions in the role editor) and deactivate or
 * reactivate them. The page renders the {@link RoleListStore}'s state and forwards intent to it.
 */
@Component({
  selector: 'app-role-list-page',
  imports: [
    AdminListPatternComponent,
    ListCellDirective,
    ListRowActionsDirective,
    EditorDialogPatternComponent,
    FieldErrorPipe,
    DgaButtonComponent,
    DgaStatusTagComponent,
    DgaTagComponent,
    DgaTextInputComponent,
    RouterLink,
    HasPermissionDirective,
    TranslatePipe,
    LastManagerDialogComponent,
    AccessWarningsComponent,
  ],
  providers: [RoleListStore],
  templateUrl: './role-list.page.html',
  styleUrl: './role-list.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RoleListPageComponent {
  protected readonly store = inject(RoleListStore);
  private readonly router = inject(Router);
  private readonly language = inject(LanguageService);

  protected readonly PermissionKeys = PermissionKeys;
  protected readonly AccessPaths = AccessPaths;
  protected readonly errorMessageKey = errorMessageKey;
  protected readonly virtualKindLabelKey = virtualKindLabelKey;

  readonly columns: readonly ColumnDef<RoleSummary>[] = [
    { key: 'name', headerKey: 'admin.access.roles.columns.name' },
    { key: 'code', headerKey: 'admin.access.roles.columns.code' },
    { key: 'isVirtual', headerKey: 'admin.access.roles.columns.type' },
    { key: 'isActive', headerKey: 'admin.access.roles.columns.status' },
    { key: 'permissionCount', headerKey: 'admin.access.roles.columns.permissions', value: permissionCountOf },
  ];
  readonly rowId = (row: RoleSummary): string => row.id;

  readonly createOpen = signal(false);
  readonly draftCode = signal('');
  readonly draftNameEn = signal('');
  readonly draftNameAr = signal('');

  constructor() {
    void this.store.load();
  }

  roleName(role: RoleSummary): string {
    return localizedName(role, this.language.current());
  }

  openCreate(): void {
    this.draftCode.set('');
    this.draftNameEn.set('');
    this.draftNameAr.set('');
    this.store.clearCreateError();
    this.createOpen.set(true);
  }

  cancelCreate(): void {
    this.createOpen.set(false);
  }

  /** Creates the role and opens it in the editor, where its permissions are chosen. */
  async submitCreate(): Promise<void> {
    const created = await this.store.create({
      code: this.draftCode(),
      nameEn: this.draftNameEn(),
      nameAr: this.draftNameAr(),
    });
    if (created) {
      this.createOpen.set(false);
      await this.router.navigate(AccessPaths.roleDetail(created.id));
    }
  }

  toggleActive(role: RoleSummary): void {
    void this.store.setActive(role, !role.isActive);
  }
}
