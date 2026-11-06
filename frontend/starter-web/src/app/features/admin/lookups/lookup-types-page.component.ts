import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LanguageService } from '../../../core/i18n/language.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { AdminListPatternComponent, ListCellDirective, type ColumnDef } from '../../../patterns';
import { ADMIN_TRANSLATED_CODES } from '../organizations/data/admin-api-error';
import { localizedName, type LookupType } from './data/lookup.models';
import { LookupTypesPageStore } from './lookup-types-page.store';
import { LookupsPaths } from './lookups.paths';

/**
 * The lookup types screen (`/admin/lookups`, `lookups.view`): the admin-list pattern over every type, each name
 * linking to the type's values. A notice says that types and values change only through migrations.
 */
@Component({
  selector: 'app-lookup-types-page',
  imports: [AdminListPatternComponent, ListCellDirective, RouterLink, TranslatePipe],
  providers: [LookupTypesPageStore],
  templateUrl: './lookup-types-page.component.html',
  styleUrl: './lookup-types-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LookupTypesPageComponent {
  private readonly language = inject(LanguageService);
  protected readonly store = inject(LookupTypesPageStore);
  protected readonly translatedCodes = ADMIN_TRANSLATED_CODES;
  protected readonly LookupsPaths = LookupsPaths;

  protected readonly columns: readonly ColumnDef<LookupType>[] = [
    { key: 'name', headerKey: 'admin.lookups.columns.name' },
    { key: 'type', headerKey: 'admin.lookups.columns.type' },
    { key: 'itemCount', headerKey: 'admin.lookups.columns.item-count' },
  ];
  protected readonly rowId = (row: LookupType): string => row.type;

  constructor() {
    void this.store.load();
  }

  protected nameOf(type: LookupType): string {
    return localizedName(type, this.language.current());
  }
}
