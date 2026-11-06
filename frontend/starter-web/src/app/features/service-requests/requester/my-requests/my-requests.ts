import type { DgaSelectOption } from '@codelab/ng-dga';
import type { ColumnDef } from '../../../../patterns';
import type { RecordListItem } from '../../../../shared/records';
import { ServiceRequestStatus } from '../../data';
import { SERVICE_REQUEST_STATUS_KEYS, requesterStatusKey } from '../requester-status';
import { ServiceRequestsRequesterTranslationKeys as Keys } from '../requester-translation-keys';

/** The column keys of my requests (the `listCell` template names). */
export const MyRequestsColumns = {
  Number: 'number',
  Status: 'status',
  Created: 'createdAt',
  Submitted: 'submittedAt',
} as const;

/** The columns: number, status (requester wording), created and submitted dates. Cells come from templates. */
export const MY_REQUESTS_COLUMNS: readonly ColumnDef<RecordListItem>[] = Object.freeze([
  { key: MyRequestsColumns.Number, headerKey: Keys.Mine.ColumnNumber },
  { key: MyRequestsColumns.Status, headerKey: Keys.Mine.ColumnStatus },
  { key: MyRequestsColumns.Created, headerKey: Keys.Mine.ColumnCreated },
  { key: MyRequestsColumns.Submitted, headerKey: Keys.Mine.ColumnSubmitted },
]);

/** The statuses the filter offers, in workflow order. */
export const MY_REQUESTS_FILTER_STATUSES: readonly ServiceRequestStatus[] = Object.freeze([
  ServiceRequestStatus.Draft,
  ServiceRequestStatus.Submitted,
  ServiceRequestStatus.UnderReview,
  ServiceRequestStatus.Returned,
  ServiceRequestStatus.Resubmitted,
  ServiceRequestStatus.Approved,
  ServiceRequestStatus.Rejected,
  ServiceRequestStatus.Cancelled,
]);

/** The status filter's options, labelled in the requester's wording (`translate` reads the active language). */
export function statusFilterOptions(translate: (key: string) => string): readonly DgaSelectOption<ServiceRequestStatus>[] {
  return MY_REQUESTS_FILTER_STATUSES.map((status) => ({
    value: status,
    label: translate(requesterStatusKey(SERVICE_REQUEST_STATUS_KEYS[status])),
  }));
}

/** The id the list tracks a row by (the PublicId). */
export function myRequestRowId(row: RecordListItem): string {
  return row.id;
}
