import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  DgaButtonComponent,
  DgaStatusTagComponent,
  DgaTabsComponent,
  DgaTextareaComponent,
  DgaTextInputComponent,
  type DgaTab,
  type DgaTabValue,
} from '@codelab/ng-dga';
import { apiErrorMessageKey } from '@codelab/ng-security';
import { LanguageService } from '../../../core/i18n/language.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import { PermissionStore } from '../../../core/permissions/permission.store';
import {
  AdminListPatternComponent,
  EditorDialogPatternComponent,
  FieldErrorPipe,
  ListCellDirective,
  ListRowActionsDirective,
  type ColumnDef,
} from '../../../patterns';
import { HasPermissionDirective } from '../../../shared/permissions/has-permission.directive';
import { LeaveDialogComponent } from '../access/common/access-dialogs.component';
import { LeaveConfirmation } from '../access/common/leave-confirmation';
import type { LeaveAware } from '../access/common/unsaved-changes.guard';
import { ADMIN_TRANSLATED_CODES } from '../organizations/data/admin-api-error';
import { formatInstant } from '../users/activation-link/format-instant';
import {
  PORTAL_CONTENT_PAGES,
  PortalContentLimits,
  PortalContentPageLabelKeys,
  SectionFields,
  isPortalContentPage,
} from './data/portal-content.models';
import { PortalContentEditorStore } from './portal-content-editor.store';
import { PortalContentPermissionKeys } from './portal-content-permissions';
import type { DraftSection } from './section-draft';

/** The id prefix of the tab panel (`dgaTabPanelId`). */
const PANEL_ID_PREFIX = 'portal-content-page';

/** The starting height of a body field, in text lines (it grows with the text). */
const BODY_ROWS = 6;

/**
 * The portal content editor (`/admin/portal-content`, `portal.content.view`): a tab per portal page (FAQ, service
 * guide, privacy, terms), the page's sections in order, and the section dialog (Arabic and English title and plain
 * body text). With `portal.content.manage` the admin adds, edits, removes and reorders sections and saves the whole
 * page at once; without it everything is read-only. Unsaved edits are guarded on tab switch, route leave and tab
 * close. The component renders the store and forwards intent.
 */
@Component({
  selector: 'app-portal-content-page',
  imports: [
    AdminListPatternComponent,
    DgaButtonComponent,
    DgaStatusTagComponent,
    DgaTabsComponent,
    DgaTextareaComponent,
    DgaTextInputComponent,
    EditorDialogPatternComponent,
    FieldErrorPipe,
    HasPermissionDirective,
    LeaveDialogComponent,
    ListCellDirective,
    ListRowActionsDirective,
    TranslatePipe,
  ],
  providers: [PortalContentEditorStore],
  templateUrl: './portal-content-page.component.html',
  styleUrl: './portal-content-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:beforeunload)': 'onBeforeUnload($event)' },
})
export class PortalContentPageComponent implements LeaveAware {
  private readonly translations = inject(TranslationService);
  private readonly language = inject(LanguageService);
  private readonly permissions = inject(PermissionStore);
  protected readonly store = inject(PortalContentEditorStore);
  protected readonly leave = new LeaveConfirmation();
  protected readonly translatedCodes = ADMIN_TRANSLATED_CODES;
  protected readonly PermissionKeys = PortalContentPermissionKeys;
  protected readonly Fields = SectionFields;
  protected readonly limits = PortalContentLimits;
  protected readonly panelIdPrefix = PANEL_ID_PREFIX;
  protected readonly bodyRows = BODY_ROWS;
  protected readonly rowId = (row: DraftSection): number => row.key;

  protected readonly columns: readonly ColumnDef<DraftSection>[] = [
    { key: 'position', headerKey: 'admin.portal-content.columns.position', value: (row) => this.positionOf(row) },
    { key: SectionFields.TitleAr, headerKey: 'admin.portal-content.columns.title-ar' },
    { key: SectionFields.TitleEn, headerKey: 'admin.portal-content.columns.title-en' },
    { key: 'state', headerKey: 'admin.portal-content.columns.state' },
  ];

  /** The selected tab; follows the store, and is set back when the admin chooses to stay on unsaved edits. */
  protected readonly tab = signal<DgaTabValue>(this.store.page());
  protected readonly tabs = computed<readonly DgaTab[]>(() =>
    PORTAL_CONTENT_PAGES.map((page) => ({ value: page, label: this.translations.translate(PortalContentPageLabelKeys[page]) })));
  protected readonly canManage = computed(() => this.permissions.has(PortalContentPermissionKeys.Manage));
  protected readonly updatedAtText = computed(() => {
    const at = this.store.updatedAt();
    return at === null ? null : formatInstant(at, this.language.current());
  });
  protected readonly saveErrorKey = computed(() => {
    const error = this.store.saveError();
    return error ? apiErrorMessageKey(error, ADMIN_TRANSLATED_CODES) : null;
  });

  constructor() {
    void this.store.load(this.store.page());
  }

  protected async choosePage(value: DgaTabValue): Promise<void> {
    if (!isPortalContentPage(value)) {
      return;
    }
    const moved = await this.store.selectPage(value, () => this.leave.ask());
    this.tab.set(moved ? value : this.store.page());
  }

  protected positionOf(section: DraftSection): number {
    return this.store.sections().indexOf(section) + 1;
  }

  protected isLast(section: DraftSection): boolean {
    return this.positionOf(section) === this.store.sections().length;
  }

  protected hasError(section: DraftSection): boolean {
    return this.store.invalidIndexes().has(this.positionOf(section) - 1);
  }

  /** The dialog's confirm: a manager applies the section to the draft; a viewer just closes it. */
  protected confirmSection(): void {
    if (this.canManage()) {
      this.store.applyEditor();
    } else {
      this.store.closeEditor();
    }
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
