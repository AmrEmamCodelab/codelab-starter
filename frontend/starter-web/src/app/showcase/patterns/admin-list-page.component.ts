import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DgaButtonComponent, DgaCheckboxComponent, DgaStatusTagComponent, DgaTextInputComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationService } from '../../core/i18n/translation.service';
import {
  AdminListPatternComponent,
  EditorDialogPatternComponent,
  ListCellDirective,
  ListRowActionsDirective,
  type ColumnDef,
  type SortState,
} from '../../patterns';
import { ShowcaseFrameComponent } from '../frame/showcase-frame.component';
import { ShowcaseState, showcaseListView } from '../frame/showcase-state';
import { ShowcaseStateSwitchComponent } from '../frame/showcase-state-switch.component';
import { ShowcasePageId } from '../showcase-pages';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';
import { ADMIN_LIST_PAGE_SIZE, ADMIN_LIST_TOTAL, ORG_FIXTURE, applyListQuery, type OrgRow } from './pattern-fixtures';

const P = ShowcaseTranslationKeys.Patterns;

/** The editor dialog's target when it creates a new row. */
export const NEW_ROW = 'new';

/** The admin-list column keys of the demo. */
export const OrgColumn = { Name: 'name', Code: 'code', Status: 'status' } as const;

/**
 * The admin list + editor dialog pattern on organisation fixtures: search and sort run on the fixture (as a server
 * would), the toolbar opens the create dialog and each row's action opens it for editing. The state switch shows the
 * loading, empty and error states.
 */
@Component({
  selector: 'app-showcase-admin-list-page',
  imports: [
    AdminListPatternComponent,
    DgaButtonComponent,
    DgaCheckboxComponent,
    DgaStatusTagComponent,
    DgaTextInputComponent,
    EditorDialogPatternComponent,
    ListCellDirective,
    ListRowActionsDirective,
    ShowcaseFrameComponent,
    ShowcaseStateSwitchComponent,
    TranslatePipe,
  ],
  templateUrl: './admin-list-page.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminListPageComponent {
  private readonly translations = inject(TranslationService);

  protected readonly pageId = ShowcasePageId.AdminList;
  protected readonly keys = ShowcaseTranslationKeys;
  protected readonly Column = OrgColumn;
  protected readonly pageSize = ADMIN_LIST_PAGE_SIZE;
  protected readonly columns: readonly ColumnDef<OrgRow>[] = [
    { key: OrgColumn.Name, headerKey: P.ColumnName, sortable: true },
    { key: OrgColumn.Code, headerKey: P.ColumnCode, sortable: true },
    { key: OrgColumn.Status, headerKey: P.ColumnStatus },
  ];
  protected readonly rowId = (row: OrgRow) => row.id;

  readonly state = signal<ShowcaseState>(ShowcaseState.Ready);
  readonly page = signal(1);
  readonly search = signal('');
  readonly sort = signal<SortState | null>(null);
  readonly onlyActive = signal(false);
  /** The row being edited, {@link NEW_ROW} for the create dialog, or null when the dialog is closed. */
  readonly editing = signal<OrgRow | typeof NEW_ROW | null>(null);
  readonly draftName = signal('');

  protected readonly view = computed(() => {
    const rows = applyListQuery(
      ORG_FIXTURE.filter(row => !this.onlyActive() || row.active),
      { search: this.search(), sort: this.sort() },
      { textOf: row => this.nameOf(row), sortValue: (row, key) => (key === OrgColumn.Code ? row.code : this.nameOf(row)) },
    );
    return showcaseListView(this.state(), rows, rows.length === ORG_FIXTURE.length ? ADMIN_LIST_TOTAL : rows.length);
  });
  protected readonly dialogTitle = computed(() => (this.editing() === NEW_ROW ? P.CreateTitle : P.EditTitle));

  openCreate(): void {
    this.draftName.set('');
    this.editing.set(NEW_ROW);
  }

  openEdit(row: OrgRow): void {
    this.draftName.set(this.nameOf(row));
    this.editing.set(row);
  }

  retry(): void {
    this.state.set(ShowcaseState.Ready);
  }

  closeEditor(): void {
    this.editing.set(null);
  }

  private nameOf(row: OrgRow): string {
    return this.translations.translate(row.nameKey);
  }
}
