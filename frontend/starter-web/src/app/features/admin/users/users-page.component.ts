import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  DgaButtonComponent,
  DgaRadioGroupComponent,
  DgaStatusTagComponent,
  DgaTextInputComponent,
} from '@codelab/ng-dga';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import { PermissionKeys } from '../../../core/permissions/permission-keys.generated';
import {
  AdminListPatternComponent,
  EditorDialogPatternComponent,
  FieldErrorPipe,
  ListCellDirective,
  ListRowActionsDirective,
  type ColumnDef,
} from '../../../patterns';
import { HasPermissionDirective } from '../../../shared/permissions/has-permission.directive';
import { AccessPaths } from '../access/access.paths';
import { ACTIVE_FILTERS, ActiveFilterLabelKeys, selectValue } from '../organizations/data/paged-list';
import { EditorMode } from '../organizations/organization-form';
import { ActivationLinkComponent } from './activation-link/activation-link.component';
import {
  USER_KINDS,
  UserKindLabelKeys,
  bindingLabelKey,
  userKindLabelKey,
  userRowActions,
  userStatusTags,
  type AdminUser,
} from './data/user.models';
import { bindingOptions, userKindOptions } from './user-form';
import { UserAction, UsersPageStore } from './users-page.store';
import { ADMIN_TRANSLATED_CODES } from '../organizations/data/admin-api-error';

/** The texts of each confirmed row action. */
export interface UserActionTexts {
  readonly titleKey: string;
  readonly textKey: string;
  readonly confirmKey: string;
  readonly destructive: boolean;
}

export const USER_ACTION_TEXTS: Readonly<Record<UserAction, UserActionTexts>> = {
  [UserAction.Deactivate]: {
    titleKey: 'admin.users.confirm.deactivate-title',
    textKey: 'admin.users.confirm.deactivate-text',
    confirmKey: 'admin.common.actions.deactivate',
    destructive: true,
  },
  [UserAction.Reactivate]: {
    titleKey: 'admin.users.confirm.reactivate-title',
    textKey: 'admin.users.confirm.reactivate-text',
    confirmKey: 'admin.common.actions.reactivate',
    destructive: false,
  },
  [UserAction.Unlock]: {
    titleKey: 'admin.users.confirm.unlock-title',
    textKey: 'admin.users.confirm.unlock-text',
    confirmKey: 'admin.users.actions.unlock',
    destructive: false,
  },
  [UserAction.IssueActivation]: {
    titleKey: 'admin.users.confirm.activation-title',
    textKey: 'admin.users.confirm.activation-text',
    confirmKey: 'admin.users.actions.issue-activation',
    destructive: true,
  },
};

/** The dialog texts when no action is pending (the closed dialog still needs a title). */
const IDLE_ACTION_TEXTS = USER_ACTION_TEXTS[UserAction.Deactivate];

/**
 * The users screen (`/admin/users`, `users.view`): the admin-list pattern with organization, kind and status filters
 * and status tags; the create / edit dialog (`users.manage`); the confirmed row actions; the activation link to hand
 * over; and a link to the user's grants (`access.grants.view`). The component renders the store's state and forwards
 * intent; the rules live in the store, the models and the API.
 */
@Component({
  selector: 'app-users-page',
  imports: [
    ActivationLinkComponent,
    AdminListPatternComponent,
    DgaButtonComponent,
    DgaRadioGroupComponent,
    DgaStatusTagComponent,
    DgaTextInputComponent,
    EditorDialogPatternComponent,
    FieldErrorPipe,
    HasPermissionDirective,
    ListCellDirective,
    ListRowActionsDirective,
    RouterLink,
    TranslatePipe,
  ],
  providers: [UsersPageStore],
  templateUrl: './users-page.component.html',
  styleUrl: './users-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsersPageComponent {
  private readonly translations = inject(TranslationService);
  protected readonly store = inject(UsersPageStore);
  /** The codes the admin bundles translate, so a module refusal shows its own text in the patterns. */
  protected readonly translatedCodes = ADMIN_TRANSLATED_CODES;
  protected readonly PermissionKeys = PermissionKeys;
  protected readonly EditorMode = EditorMode;
  protected readonly UserAction = UserAction;
  protected readonly AccessPaths = AccessPaths;
  protected readonly kinds = USER_KINDS;
  protected readonly kindLabelKeys = UserKindLabelKeys;
  protected readonly activeFilters = ACTIVE_FILTERS;
  protected readonly activeFilterLabelKeys = ActiveFilterLabelKeys;
  protected readonly kindLabelKey = userKindLabelKey;
  protected readonly bindingLabelKey = bindingLabelKey;
  protected readonly statusTags = userStatusTags;
  protected readonly rowActions = userRowActions;
  protected readonly selectValue = selectValue;

  protected readonly columns: readonly ColumnDef<AdminUser>[] = [
    { key: 'userName', headerKey: 'admin.users.columns.user-name' },
    { key: 'displayName', headerKey: 'admin.users.columns.display-name' },
    { key: 'email', headerKey: 'admin.users.columns.email' },
    { key: 'organizationName', headerKey: 'admin.users.columns.organization' },
    { key: 'kind', headerKey: 'admin.users.columns.kind' },
    { key: 'binding', headerKey: 'admin.users.columns.binding' },
    { key: 'status', headerKey: 'admin.users.columns.status' },
  ];
  protected readonly rowId = (row: AdminUser): string => row.id;
  protected readonly kindOptions = computed(() => userKindOptions((key) => this.translations.translate(key)));
  protected readonly bindingOptions = computed(() => bindingOptions((key) => this.translations.translate(key)));
  protected readonly actionTexts = computed(() => {
    const pending = this.store.action();
    return pending === null ? IDLE_ACTION_TEXTS : USER_ACTION_TEXTS[pending.action];
  });
}
