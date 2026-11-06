import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  DgaAlertComponent,
  DgaAlertVariant,
  DgaButtonComponent,
  DgaIconComponent,
  DgaSelectComponent,
  DgaStatusTagComponent,
  type DgaSelectOption,
} from '@codelab/ng-dga';
import { apiErrorMessageKey, type ApiError } from '@codelab/ng-security';
import { LanguageService } from '../../../core/i18n/language.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import { ListCellDirective, ListRowActionsDirective, WorkQueuePatternComponent, type ColumnDef } from '../../../patterns';
import { HasPermissionDirective } from '../../../shared/permissions/has-permission.directive';
import {
  RECORDS_TRANSLATED_CODES,
  SlaBadgeComponent,
  WorkflowAudience,
  formatRecordInstant,
  type WorkQueueItem,
} from '../../../shared/records';
import {
  SERVICE_REQUEST_STATUS_KEYS,
  STAFF_QUEUE_STATUSES,
  serviceRequestStatusTone,
  serviceRequestStatusWording,
} from './service-request-status-display';
import { ServiceRequestQueueStore } from './service-request-queue.store';
import { ServiceRequestsStaffPaths } from './service-requests-staff.paths';
import { ServiceRequestsStaffPermissionKeys } from './service-requests-staff.permissions';
import { ServiceRequestsStaffTranslationKeys } from './service-requests-staff-translation-keys';
import { AssignmentHolder } from './staff-decisions';

const Keys = ServiceRequestsStaffTranslationKeys;

/** The queue's column keys (also the `listCell` names of their templates). */
export const StaffQueueColumn = {
  Number: 'number',
  Status: 'status',
  Owner: 'ownerDisplayName',
  SubmittedAt: 'submittedAt',
  Assignee: 'assignee',
  Sla: 'sla',
} as const;

/**
 * The staff work queue of service requests (`/staff/service-requests`), on the work-queue pattern over the generic
 * Queues API: views unassigned / mine / all, a status filter, the number search, paging, the server's SLA badge, and
 * claim / release per row (`assignments.work`). The number opens the staff detail, where the decisions are taken.
 */
@Component({
  selector: 'app-service-request-queue-page',
  imports: [
    DgaAlertComponent,
    DgaButtonComponent,
    DgaIconComponent,
    DgaSelectComponent,
    DgaStatusTagComponent,
    HasPermissionDirective,
    ListCellDirective,
    ListRowActionsDirective,
    RouterLink,
    SlaBadgeComponent,
    TranslatePipe,
    WorkQueuePatternComponent,
  ],
  providers: [ServiceRequestQueueStore],
  templateUrl: './service-request-queue-page.component.html',
  styleUrl: './service-request-queue-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ServiceRequestQueuePageComponent {
  protected readonly store = inject(ServiceRequestQueueStore);
  private readonly translations = inject(TranslationService);
  private readonly language = inject(LanguageService);

  protected readonly keys = Keys;
  protected readonly Column = StaffQueueColumn;
  protected readonly Holder = AssignmentHolder;
  protected readonly permissionKeys = ServiceRequestsStaffPermissionKeys;
  protected readonly paths = ServiceRequestsStaffPaths;
  protected readonly AlertVariant = DgaAlertVariant;
  protected readonly translatedCodes = RECORDS_TRANSLATED_CODES;
  protected readonly columns: readonly ColumnDef<WorkQueueItem>[] = [
    { key: StaffQueueColumn.Number, headerKey: Keys.Queue.ColumnNumber },
    { key: StaffQueueColumn.Status, headerKey: Keys.Queue.ColumnStatus },
    { key: StaffQueueColumn.Owner, headerKey: Keys.Queue.ColumnOwner },
    { key: StaffQueueColumn.SubmittedAt, headerKey: Keys.Queue.ColumnSubmittedAt },
    { key: StaffQueueColumn.Assignee, headerKey: Keys.Queue.ColumnAssignee },
    { key: StaffQueueColumn.Sla, headerKey: Keys.Queue.ColumnSla },
  ];
  protected readonly rowId = (row: WorkQueueItem): string => row.id;

  protected readonly statusOptions = computed<DgaSelectOption<number>[]>(() => {
    this.language.current();
    return STAFF_QUEUE_STATUSES.map((status) => ({
      value: status,
      label: this.translations.translate(serviceRequestStatusWording(SERVICE_REQUEST_STATUS_KEYS[status], WorkflowAudience.Staff)),
    }));
  });

  /** The staff wording key of a row's status. */
  protected statusLabelKey(statusKey: string): string {
    return serviceRequestStatusWording(statusKey, WorkflowAudience.Staff);
  }

  protected statusTone(status: number) {
    return serviceRequestStatusTone(status);
  }

  protected instantText(iso: string): string {
    return formatRecordInstant(iso, this.language.current());
  }

  protected messageKey(error: ApiError): string {
    return apiErrorMessageKey(error, RECORDS_TRANSLATED_CODES);
  }
}
