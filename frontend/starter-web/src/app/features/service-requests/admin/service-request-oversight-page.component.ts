import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DgaAlertComponent, DgaAlertVariant, DgaSelectComponent, DgaStatusTagComponent, type DgaSelectOption } from '@codelab/ng-dga';
import { LanguageService } from '../../../core/i18n/language.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import { AdminListPatternComponent, ListCellDirective, type ColumnDef } from '../../../patterns';
import { RECORDS_TRANSLATED_CODES, WorkflowAudience, formatRecordInstant, type OversightListItem } from '../../../shared/records';
import {
  ALL_SERVICE_REQUEST_STATUSES,
  SERVICE_REQUEST_STATUS_KEYS,
  serviceRequestStatusTone,
  serviceRequestStatusWording,
} from '../staff/service-request-status-display';
import { ServiceRequestOversightStore } from './service-request-oversight.store';
import { ServiceRequestsAdminPaths } from './service-requests-admin.paths';
import { ServiceRequestsAdminTranslationKeys } from './service-requests-admin-translation-keys';

const Keys = ServiceRequestsAdminTranslationKeys.List;

/** The oversight list's column keys (also the `listCell` names of their templates). */
export const OversightColumn = {
  Number: 'number',
  Status: 'status',
  Owner: 'ownerDisplayName',
  SubmittedAt: 'submittedAt',
  UpdatedAt: 'updatedAt',
} as const;

/**
 * The admin "all requests" list (`/admin/requests`, `records.oversight.view`), on the admin-list pattern over
 * `GET api/records/service-request/oversight`: every request of every requester, status in the admin wording, a
 * status filter, the number search and paging. READ-ONLY: no toolbar button and no row action; a row's number opens
 * the read-only detail.
 */
@Component({
  selector: 'app-service-request-oversight-page',
  imports: [
    AdminListPatternComponent,
    DgaAlertComponent,
    DgaSelectComponent,
    DgaStatusTagComponent,
    ListCellDirective,
    RouterLink,
    TranslatePipe,
  ],
  providers: [ServiceRequestOversightStore],
  templateUrl: './service-request-oversight-page.component.html',
  styleUrl: './service-request-oversight-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ServiceRequestOversightPageComponent {
  protected readonly store = inject(ServiceRequestOversightStore);
  private readonly translations = inject(TranslationService);
  private readonly language = inject(LanguageService);

  protected readonly keys = Keys;
  protected readonly Column = OversightColumn;
  protected readonly paths = ServiceRequestsAdminPaths;
  protected readonly AlertVariant = DgaAlertVariant;
  protected readonly translatedCodes = RECORDS_TRANSLATED_CODES;
  protected readonly columns: readonly ColumnDef<OversightListItem>[] = [
    { key: OversightColumn.Number, headerKey: Keys.ColumnNumber },
    { key: OversightColumn.Status, headerKey: Keys.ColumnStatus },
    { key: OversightColumn.Owner, headerKey: Keys.ColumnOwner },
    { key: OversightColumn.SubmittedAt, headerKey: Keys.ColumnSubmittedAt },
    { key: OversightColumn.UpdatedAt, headerKey: Keys.ColumnUpdatedAt },
  ];
  protected readonly rowId = (row: OversightListItem): string => row.id;

  protected readonly statusOptions = computed<DgaSelectOption<number>[]>(() => {
    this.language.current();
    return ALL_SERVICE_REQUEST_STATUSES.map((status) => ({
      value: status,
      label: this.translations.translate(serviceRequestStatusWording(SERVICE_REQUEST_STATUS_KEYS[status], WorkflowAudience.Admin)),
    }));
  });

  /** The admin wording key of a row's status. */
  protected statusLabelKey(statusKey: string): string {
    return serviceRequestStatusWording(statusKey, WorkflowAudience.Admin);
  }

  protected statusTone(status: number) {
    return serviceRequestStatusTone(status);
  }

  protected instantText(iso: string): string {
    return formatRecordInstant(iso, this.language.current());
  }
}
