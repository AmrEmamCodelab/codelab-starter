import { ChangeDetectionStrategy, Component, computed, effect, inject, input, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  DgaAlertComponent,
  DgaAlertVariant,
  DgaButtonComponent,
  DgaIconComponent,
  DgaStatusTagComponent,
} from '@codelab/ng-dga';
import { apiErrorMessageKey, type ApiError } from '@codelab/ng-security';
import { LanguageService } from '../../../core/i18n/language.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { ActionTone, DECISION_REASON_FIELD, DecisionDialogComponent, StaffDetailPatternComponent } from '../../../patterns';
import { HasPermissionDirective } from '../../../shared/permissions/has-permission.directive';
import { RecordAttachmentsComponent } from '../../../shared/record-attachments/record-attachments.component';
import {
  RECORDS_TRANSLATED_CODES,
  RecordDetailCardsComponent,
  RecordTimelineComponent,
  SlaBadgeComponent,
  StatusWordingPipe,
  WorkflowAudience,
} from '../../../shared/records';
import { SERVICE_REQUEST_ATTACHABLE_RECORD_TYPE } from '../data';
import { serviceRequestDetailCards } from './service-request-record-card';
import { serviceRequestActionLabelKey, serviceRequestActionTone, serviceRequestStatusTone, serviceRequestStatusWording } from './service-request-status-display';
import { ServiceRequestStaffDetailStore } from './service-request-staff-detail.store';
import { ServiceRequestsStaffPaths } from './service-requests-staff.paths';
import { ServiceRequestsStaffPermissionKeys } from './service-requests-staff.permissions';
import { ServiceRequestsStaffTranslationKeys } from './service-requests-staff-translation-keys';
import { STAFF_REASON_LIMITS } from './staff-decisions';

/** The heading level of the detail cards and the attachments section (below the page's h1). */
const SECTION_HEADING_LEVEL = 2;
/** The timeline sits inside the pattern's history section (h2). */
const TIMELINE_HEADING_LEVEL = 3;

/**
 * The staff detail of one service request (`/staff/service-requests/:id`), on the staff-detail pattern: the status in
 * the staff wording with the SLA badge, the assignee with claim / release (`assignments.work`), the request's cards,
 * its attachments read-only, the timeline, and the decision buttons from the server's allowed actions. Return and
 * reject (Reason Required) open the decision dialog, which refuses a blank reason; a terminal request offers nothing.
 */
@Component({
  selector: 'app-service-request-staff-detail-page',
  imports: [
    DecisionDialogComponent,
    DgaAlertComponent,
    DgaButtonComponent,
    DgaIconComponent,
    DgaStatusTagComponent,
    HasPermissionDirective,
    RecordAttachmentsComponent,
    RecordDetailCardsComponent,
    RecordTimelineComponent,
    RouterLink,
    SlaBadgeComponent,
    StaffDetailPatternComponent,
    StatusWordingPipe,
    TranslatePipe,
  ],
  providers: [ServiceRequestStaffDetailStore],
  templateUrl: './service-request-staff-detail-page.component.html',
  styleUrl: './service-request-staff-detail-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ServiceRequestStaffDetailPageComponent {
  /** The route's `:id` (the request's PublicId). */
  readonly id = input.required<string>();

  protected readonly store = inject(ServiceRequestStaffDetailStore);
  private readonly language = inject(LanguageService);

  protected readonly keys = ServiceRequestsStaffTranslationKeys;
  protected readonly paths = ServiceRequestsStaffPaths;
  protected readonly permissionKeys = ServiceRequestsStaffPermissionKeys;
  protected readonly audience = WorkflowAudience.Staff;
  protected readonly AlertVariant = DgaAlertVariant;
  protected readonly translatedCodes = RECORDS_TRANSLATED_CODES;
  protected readonly attachableType = SERVICE_REQUEST_ATTACHABLE_RECORD_TYPE;
  protected readonly reasonLimits = STAFF_REASON_LIMITS;
  protected readonly sectionHeadingLevel = SECTION_HEADING_LEVEL;
  protected readonly timelineHeadingLevel = TIMELINE_HEADING_LEVEL;
  protected readonly statusKeyFor = serviceRequestStatusWording;
  protected readonly actionLabelKeyFor = serviceRequestActionLabelKey;

  protected readonly titleParams = computed(() => ({ number: this.store.detail()?.number ?? '' }));
  protected readonly statusTone = computed(() => serviceRequestStatusTone(this.store.detail()?.status ?? 0));
  protected readonly cards = computed(() => {
    const detail = this.store.detail();
    const details = this.store.details();
    return detail === null || details === null ? [] : serviceRequestDetailCards(detail, details, this.language.current());
  });
  protected readonly pendingDestructive = computed(() => {
    const pending = this.store.pending();
    return pending !== null && serviceRequestActionTone(pending.actionKey) === ActionTone.Destructive;
  });
  protected readonly reasonField = computed(() => this.store.pending()?.reasonFieldKey ?? DECISION_REASON_FIELD);
  /**
   * The refusals shown above the page: a failed claim or release, and a refused decision taken without the dialog (a
   * refusal inside the dialog shows there).
   */
  protected readonly actionErrors = computed<readonly ApiError[]>(() => {
    const decideError = this.store.pending() === null ? this.store.decideError() : null;
    return [this.store.assignError(), decideError].filter((failure): failure is ApiError => failure !== null);
  });

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => void this.store.open(id));
    });
  }

  protected messageKey(error: ApiError): string {
    return apiErrorMessageKey(error, RECORDS_TRANSLATED_CODES);
  }
}
