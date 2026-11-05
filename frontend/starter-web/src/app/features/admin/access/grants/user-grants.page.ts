import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  DgaButtonComponent,
  DgaCheckboxComponent,
  DgaRadioGroupComponent,
  DgaStatusTagComponent,
  DgaTagComponent,
  type DgaRadioOption,
  type DgaRadioValue,
} from '@codelab/ng-dga';
import { tryAsId } from '@codelab/ng-security';
import { LanguageService } from '../../../../core/i18n/language.service';
import { TranslatePipe } from '../../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../../core/i18n/translation.service';
import { PermissionKeys } from '../../../../core/permissions/permission-keys.generated';
import { PermissionStore } from '../../../../core/permissions/permission.store';
import { RoutePaths } from '../../../../core/routing/route-paths';
import { TreeEditorPatternComponent } from '../../../../patterns';
import { HasPermissionDirective } from '../../../../shared/permissions/has-permission.directive';
import { AccessRouteParams } from '../access.paths';
import {
  AccessWarningsComponent,
  LastManagerDialogComponent,
  LeaveDialogComponent,
} from '../common/access-dialogs.component';
import { errorMessageKey } from '../common/access-errors';
import {
  labelText,
  localizedName,
  permissionLabelResolver,
  scopeLabelKey,
  sourceLabelKey,
  userKindLabelKey,
  virtualKindLabelKey,
  type BilingualName,
} from '../common/access-labels';
import { LeaveConfirmation } from '../common/leave-confirmation';
import type { LeaveAware } from '../common/unsaved-changes.guard';
import {
  AccessErrorCodes,
  AccessIdDomains,
  PERMISSION_SCOPES,
  toPermissionScope,
  type RoleId,
} from '../data/access.models';
import type { EffectiveSource } from './grants-draft';
import { UserGrantsStore } from './user-grants.store';

/** The root segment every absolute in-app link starts from. */
const ROOT_SEGMENT = '/';

/**
 * The grant editor of one user (`/admin/access/users/:userId/grants`, `access.grants.view`): the assigned roles
 * with a scope each, direct permissions on the tree-editor pattern with a scope each, the virtual roles that apply
 * (read-only) and the effective permissions with their sources. Without `access.grants.manage`, and for the
 * break-glass administrator, it is read-only. A change is live on the user's next request (no relogin).
 */
@Component({
  selector: 'app-user-grants-page',
  imports: [
    TreeEditorPatternComponent,
    DgaButtonComponent,
    DgaCheckboxComponent,
    DgaRadioGroupComponent,
    DgaStatusTagComponent,
    DgaTagComponent,
    RouterLink,
    HasPermissionDirective,
    TranslatePipe,
    AccessWarningsComponent,
    LastManagerDialogComponent,
    LeaveDialogComponent,
  ],
  providers: [UserGrantsStore],
  templateUrl: './user-grants.page.html',
  styleUrl: './user-grants.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:beforeunload)': 'onBeforeUnload($event)' },
})
export class UserGrantsPageComponent implements LeaveAware {
  protected readonly store = inject(UserGrantsStore);
  private readonly language = inject(LanguageService);
  private readonly translations = inject(TranslationService);
  private readonly permissions = inject(PermissionStore);

  protected readonly PermissionKeys = PermissionKeys;
  protected readonly adminLink: readonly string[] = [ROOT_SEGMENT, RoutePaths.Admin];
  protected readonly errorMessageKey = errorMessageKey;
  protected readonly scopeLabelKey = scopeLabelKey;
  protected readonly userKindLabelKey = userKindLabelKey;
  protected readonly virtualKindLabelKey = virtualKindLabelKey;
  protected readonly toPermissionScope = toPermissionScope;
  protected readonly leave = new LeaveConfirmation();

  readonly invalidId = signal(false);
  readonly notFoundKey = errorMessageKey(AccessErrorCodes.UserNotFound);

  readonly canManage = computed(() => this.permissions.has(PermissionKeys.AccessGrantsManage));
  /** Editing is off without the manage key and for the break-glass administrator (the server refuses it). */
  readonly readOnly = computed(() => !this.canManage() || this.store.grants()?.user.isBreakGlass === true);
  readonly labelFor = computed(() =>
    permissionLabelResolver(this.store.catalogByKey(), (key) => this.translations.has(key)));
  readonly scopeOptions = computed<readonly DgaRadioOption[]>(() =>
    PERMISSION_SCOPES.map((scope) => ({ value: scope, label: this.translations.translate(scopeLabelKey(scope)) })));

  constructor() {
    const raw = inject(ActivatedRoute).snapshot.paramMap.get(AccessRouteParams.UserId);
    const userId = tryAsId(AccessIdDomains.User, raw);
    if (userId === null) {
      this.invalidId.set(true);
    } else {
      void this.store.load(userId, { withRolesAndCatalog: this.permissions.has(PermissionKeys.AccessRolesView) });
    }
  }

  name(named: BilingualName): string {
    return localizedName(named, this.language.current());
  }

  permissionText(key: string): string {
    return labelText(this.labelFor()(key), (translationKey) => this.translations.translate(translationKey));
  }

  sourceText(source: EffectiveSource): string {
    const roleName = source.role ? this.name(source.role) : '';
    return this.translations.translate(sourceLabelKey(source.kind), { name: roleName });
  }

  roleScope(roleId: RoleId): number | null {
    return this.store.roleDraft().get(roleId) ?? null;
  }

  directScope(key: string): number | null {
    return this.store.directDraft().get(key) ?? null;
  }

  onRoleScope(roleId: RoleId, value: DgaRadioValue | null): void {
    if (typeof value === 'number') {
      this.store.setRoleScope(roleId, toPermissionScope(value));
    }
  }

  onDirectScope(key: string, value: DgaRadioValue | null): void {
    if (typeof value === 'number') {
      this.store.setDirectScope(key, toPermissionScope(value));
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
