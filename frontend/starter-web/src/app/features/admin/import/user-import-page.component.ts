import { ChangeDetectionStrategy, Component, computed, inject, viewChild } from '@angular/core';
import { DgaButtonComponent, DgaCardComponent, DgaIconComponent, DgaStatusTagComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { PermissionKeys } from '../../../core/permissions/permission-keys.generated';
import {
  AdminListPatternComponent,
  ListCellDirective,
  allFieldErrors,
  fieldErrorsFrom,
  type ColumnDef,
} from '../../../patterns';
import { FilePickerComponent } from '../../../shared/file-picker/file-picker.component';
import { HasPermissionDirective } from '../../../shared/permissions/has-permission.directive';
import { adminErrorMessageKey, errorCodeKey } from '../organizations/data/admin-api-error';
import { ActivationLinkComponent } from '../users/activation-link/activation-link.component';
import { bindingLabelKey, userKindLabelKey } from '../users/data/user.models';
import type { ImportPreviewRow, ImportedUser, RejectedImportRow } from './data/import.models';
import { IMPORT_FILE_ACCEPT, IMPORT_MAX_UPLOAD_MEBIBYTES, ImportFileProblemKeys } from './import-file';
import { ImportStep, UserImportStore } from './user-import.store';

/**
 * The Excel user import screen (`/admin/import`, `users.import`): pick one workbook, preview every row with its
 * verdict and reasons (the admin-list pattern, paged on the client), commit the valid rows, then hand over each
 * password-bound account's activation link. The component renders the store's state and forwards intent.
 */
@Component({
  selector: 'app-user-import-page',
  imports: [
    ActivationLinkComponent,
    AdminListPatternComponent,
    DgaButtonComponent,
    DgaCardComponent,
    DgaIconComponent,
    DgaStatusTagComponent,
    FilePickerComponent,
    HasPermissionDirective,
    ListCellDirective,
    TranslatePipe,
  ],
  providers: [UserImportStore],
  templateUrl: './user-import-page.component.html',
  styleUrl: './user-import-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserImportPageComponent {
  private readonly filePicker = viewChild.required(FilePickerComponent);
  protected readonly store = inject(UserImportStore);
  protected readonly PermissionKeys = PermissionKeys;
  protected readonly ImportStep = ImportStep;
  protected readonly accept = IMPORT_FILE_ACCEPT;
  protected readonly maxMebibytes = IMPORT_MAX_UPLOAD_MEBIBYTES;
  protected readonly problemKeys = ImportFileProblemKeys;
  protected readonly kindLabelKey = userKindLabelKey;
  protected readonly bindingLabelKey = bindingLabelKey;
  protected readonly errorCodeKey = errorCodeKey;

  protected readonly previewColumns: readonly ColumnDef<ImportPreviewRow>[] = [
    { key: 'rowNumber', headerKey: 'admin.import.columns.row' },
    { key: 'userName', headerKey: 'admin.import.columns.user-name' },
    { key: 'displayName', headerKey: 'admin.import.columns.display-name' },
    { key: 'email', headerKey: 'admin.import.columns.email' },
    { key: 'kind', headerKey: 'admin.import.columns.kind' },
    { key: 'binding', headerKey: 'admin.import.columns.binding' },
    { key: 'organizationCode', headerKey: 'admin.import.columns.organization' },
    { key: 'valid', headerKey: 'admin.import.columns.verdict' },
  ];
  protected readonly createdColumns: readonly ColumnDef<ImportedUser>[] = [
    { key: 'userName', headerKey: 'admin.import.columns.user-name' },
    { key: 'binding', headerKey: 'admin.import.columns.binding' },
    { key: 'activationLink', headerKey: 'admin.import.columns.activation' },
  ];
  protected readonly rejectedColumns: readonly ColumnDef<RejectedImportRow>[] = [
    { key: 'rowNumber', headerKey: 'admin.import.columns.row' },
    { key: 'errors', headerKey: 'admin.import.columns.reasons' },
  ];
  protected readonly previewRowId = (row: ImportPreviewRow): string | number => row.rowNumber;
  protected readonly createdRowId = (row: ImportedUser): string => row.id;
  protected readonly rejectedRowId = (row: RejectedImportRow): string | number => row.rowNumber;

  protected readonly errorMessageKey = computed(() => {
    const error = this.store.error();
    return error === null ? null : adminErrorMessageKey(error);
  });
  /** The field errors of a refused upload (a missing column names the column). */
  protected readonly errorDetails = computed(() => allFieldErrors(fieldErrorsFrom(this.store.error())));

  /** The result lists show every row on one page (a pager needs a page size of at least 1). */
  resultPageSize(count: number): number {
    return Math.max(count, 1);
  }

  startOver(): void {
    this.filePicker().clear();
    this.store.startOver();
  }
}
