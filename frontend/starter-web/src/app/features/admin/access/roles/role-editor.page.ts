import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { DgaButtonComponent, DgaStatusTagComponent, DgaTagComponent, DgaTextInputComponent } from '@codelab/ng-dga';
import { tryAsId } from '@codelab/ng-security';
import { LanguageService } from '../../../../core/i18n/language.service';
import { TranslatePipe } from '../../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../../core/i18n/translation.service';
import { PermissionKeys } from '../../../../core/permissions/permission-keys.generated';
import { PermissionStore } from '../../../../core/permissions/permission.store';
import { FieldErrorPipe, TreeEditorPatternComponent } from '../../../../patterns';
import { HasPermissionDirective } from '../../../../shared/permissions/has-permission.directive';
import { AccessPaths, AccessRouteParams } from '../access.paths';
import {
  AccessWarningsComponent,
  LastManagerDialogComponent,
  LeaveDialogComponent,
} from '../common/access-dialogs.component';
import { errorMessageKey } from '../common/access-errors';
import { localizedName, permissionLabelResolver, virtualKindLabelKey } from '../common/access-labels';
import { LeaveConfirmation } from '../common/leave-confirmation';
import type { LeaveAware } from '../common/unsaved-changes.guard';
import { AccessErrorCodes, AccessIdDomains } from '../data/access.models';
import { RoleEditorStore } from './role-editor.store';

/**
 * The role editor (`/admin/access/roles/:roleId`, `access.roles.view`): the role's names and its permissions on
 * the tree-editor pattern, built from the catalog's dotted keys. Retired keys the role still lists are shown
 * disabled and dropped on save. Virtual roles are editable but marked as applied automatically. Without
 * `access.roles.manage` everything is read-only. Unsaved edits are guarded on leave (route and tab close).
 */
@Component({
  selector: 'app-role-editor-page',
  imports: [
    TreeEditorPatternComponent,
    FieldErrorPipe,
    DgaButtonComponent,
    DgaStatusTagComponent,
    DgaTagComponent,
    DgaTextInputComponent,
    RouterLink,
    HasPermissionDirective,
    TranslatePipe,
    AccessWarningsComponent,
    LastManagerDialogComponent,
    LeaveDialogComponent,
  ],
  providers: [RoleEditorStore],
  templateUrl: './role-editor.page.html',
  styleUrl: './role-editor.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:beforeunload)': 'onBeforeUnload($event)' },
})
export class RoleEditorPageComponent implements LeaveAware {
  protected readonly store = inject(RoleEditorStore);
  private readonly language = inject(LanguageService);
  private readonly translations = inject(TranslationService);
  private readonly permissions = inject(PermissionStore);

  protected readonly PermissionKeys = PermissionKeys;
  protected readonly AccessPaths = AccessPaths;
  protected readonly errorMessageKey = errorMessageKey;
  protected readonly virtualKindLabelKey = virtualKindLabelKey;
  protected readonly leave = new LeaveConfirmation();

  /** The route id was not a PublicId: nothing to load. */
  readonly invalidId = signal(false);
  readonly notFoundKey = errorMessageKey(AccessErrorCodes.RoleNotFound);

  readonly canManage = computed(() => this.permissions.has(PermissionKeys.AccessRolesManage));
  readonly roleName = computed(() => {
    const role = this.store.role();
    return role ? localizedName(role, this.language.current()) : '';
  });
  readonly labelFor = computed(() =>
    permissionLabelResolver(this.store.catalogByKey(), (key) => this.translations.has(key)));
  readonly retiredValue = computed(() => new Set(this.store.retiredHeld()));

  constructor() {
    const roleId = tryAsId(AccessIdDomains.Role, inject(ActivatedRoute).snapshot.paramMap.get(AccessRouteParams.RoleId));
    if (roleId === null) {
      this.invalidId.set(true);
    } else {
      void this.store.load(roleId);
    }
  }

  save(): void {
    void this.store.save();
  }

  discard(): void {
    this.store.reset();
  }

  canLeave(): boolean | Promise<boolean> {
    return this.store.dirty() ? this.leave.ask() : true;
  }

  onBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.store.dirty()) {
      event.preventDefault();
    }
  }
}
