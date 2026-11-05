import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import {
  DgaButtonComponent,
  DgaCheckboxComponent,
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
import {
  ORGANIZATION_KINDS,
  OrganizationKindLabelKeys,
  organizationKindLabelKey,
  type Organization,
} from './data/organization.models';
import { ACTIVE_FILTERS, ActiveFilterLabelKeys, selectValue } from './data/paged-list';
import { EditorMode, organizationKindOptions } from './organization-form';
import { OrganizationsPageStore } from './organizations-page.store';
import { ADMIN_TRANSLATED_CODES } from './data/admin-api-error';

/**
 * The organizations screen (`/admin/organizations`, `organizations.view`): the admin-list pattern with kind and
 * status filters, the editor dialog for create and edit (`organizations.manage`), and the deactivate / reactivate
 * confirmation. The component renders the store's state and forwards intent; the rules live in the store and the API.
 */
@Component({
  selector: 'app-organizations-page',
  imports: [
    AdminListPatternComponent,
    DgaButtonComponent,
    DgaCheckboxComponent,
    DgaRadioGroupComponent,
    DgaStatusTagComponent,
    DgaTextInputComponent,
    EditorDialogPatternComponent,
    FieldErrorPipe,
    HasPermissionDirective,
    ListCellDirective,
    ListRowActionsDirective,
    TranslatePipe,
  ],
  providers: [OrganizationsPageStore],
  templateUrl: './organizations-page.component.html',
  styleUrl: './organizations-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrganizationsPageComponent {
  private readonly translations = inject(TranslationService);
  protected readonly store = inject(OrganizationsPageStore);
  /** The codes the admin bundles translate, so a module refusal shows its own text in the patterns. */
  protected readonly translatedCodes = ADMIN_TRANSLATED_CODES;
  protected readonly PermissionKeys = PermissionKeys;
  protected readonly EditorMode = EditorMode;
  protected readonly kinds = ORGANIZATION_KINDS;
  protected readonly kindLabelKeys = OrganizationKindLabelKeys;
  protected readonly activeFilters = ACTIVE_FILTERS;
  protected readonly activeFilterLabelKeys = ActiveFilterLabelKeys;
  protected readonly kindLabelKey = organizationKindLabelKey;
  protected readonly selectValue = selectValue;

  protected readonly columns: readonly ColumnDef<Organization>[] = [
    { key: 'code', headerKey: 'admin.organizations.columns.code' },
    { key: 'nameAr', headerKey: 'admin.organizations.columns.name-ar' },
    { key: 'nameEn', headerKey: 'admin.organizations.columns.name-en' },
    { key: 'kind', headerKey: 'admin.organizations.columns.kind' },
    { key: 'isActive', headerKey: 'admin.organizations.columns.status' },
  ];
  protected readonly rowId = (row: Organization): string => row.id;
  protected readonly kindOptions = computed(() => organizationKindOptions((key) => this.translations.translate(key)));
}
