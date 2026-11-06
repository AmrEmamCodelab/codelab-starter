import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { DgaButtonComponent, DgaStatusTagComponent } from '@codelab/ng-dga';
import { LanguageService } from '../../../core/i18n/language.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import {
  AdminListPatternComponent,
  EditorDialogPatternComponent,
  ListCellDirective,
  ListRowActionsDirective,
  type ColumnDef,
} from '../../../patterns';
import { HasPermissionDirective } from '../../../shared/permissions/has-permission.directive';
import { ADMIN_TRANSLATED_CODES } from '../organizations/data/admin-api-error';
import { localizedName, type AdminLookupItem } from './data/lookup.models';
import { LookupItemsPageStore } from './lookup-items-page.store';
import { LookupsPermissionKeys } from './lookups-permissions';
import { LookupsPaths, LookupsRouteParams } from './lookups.paths';

/**
 * One lookup type's values (`/admin/lookups/:type`, `lookups.view`): code, Arabic and English names, sort order and
 * an active tag, with a confirmed activate / deactivate per value (`lookups.manage`). Values are never created,
 * renamed or deleted here: that is a migration (`/new-lookup`), as the notice says.
 */
@Component({
  selector: 'app-lookup-items-page',
  imports: [
    AdminListPatternComponent,
    DgaButtonComponent,
    DgaStatusTagComponent,
    EditorDialogPatternComponent,
    HasPermissionDirective,
    ListCellDirective,
    ListRowActionsDirective,
    RouterLink,
    TranslatePipe,
  ],
  providers: [LookupItemsPageStore],
  templateUrl: './lookup-items-page.component.html',
  styleUrl: './lookup-items-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LookupItemsPageComponent {
  private readonly language = inject(LanguageService);
  protected readonly store = inject(LookupItemsPageStore);
  protected readonly translatedCodes = ADMIN_TRANSLATED_CODES;
  protected readonly LookupsPaths = LookupsPaths;
  protected readonly LookupsPermissionKeys = LookupsPermissionKeys;

  protected readonly columns: readonly ColumnDef<AdminLookupItem>[] = [
    { key: 'code', headerKey: 'admin.lookups.items.columns.code' },
    { key: 'nameAr', headerKey: 'admin.lookups.items.columns.name-ar' },
    { key: 'nameEn', headerKey: 'admin.lookups.items.columns.name-en' },
    { key: 'sortOrder', headerKey: 'admin.lookups.items.columns.sort-order' },
    { key: 'isActive', headerKey: 'admin.lookups.items.columns.status' },
  ];
  protected readonly rowId = (row: AdminLookupItem): number => row.id;
  /** The type's name in the current language, else its key until the names arrive. */
  protected readonly typeName = computed(() => {
    const info = this.store.typeInfo();
    return info ? localizedName(info, this.language.current()) : (this.store.type() ?? '');
  });

  constructor() {
    const type = inject(ActivatedRoute).snapshot.paramMap.get(LookupsRouteParams.Type) ?? '';
    void this.store.open(type);
  }
}
