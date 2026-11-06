import { ChangeDetectionStrategy, Component, type OnInit, computed, inject, input } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { DgaAlertComponent, DgaButtonComponent, type DgaStatusTone } from '@codelab/ng-dga';
import { HttpStatus, type ApiError } from '@codelab/ng-security';
import { LanguageService } from '../../../../core/i18n/language.service';
import { TranslatePipe } from '../../../../core/i18n/translate.pipe';
import {
  RequesterDetailActionsDirective,
  RequesterDetailAttachmentsDirective,
  RequesterDetailPatternComponent,
} from '../../../../patterns';
import { HasPermissionDirective } from '../../../../shared/permissions/has-permission.directive';
import { RecordAttachmentsComponent } from '../../../../shared/record-attachments/record-attachments.component';
import {
  DecisionPanelComponent,
  RECORDS_TRANSLATED_CODES,
  RecordDetailCardsComponent,
  RecordTimelineComponent,
  RecordsErrorCodes,
  statusWordingKey,
  type RecordDecision,
} from '../../../../shared/records';
import {
  SERVICE_REQUEST_ATTACHABLE_RECORD_TYPE,
  serviceRequestCardsAdapter,
  type ServiceRequestId,
} from '../../data';
import { SERVICE_REQUESTS_REQUESTER_BASE_LINK, serviceRequestIdOf, serviceRequestsRequesterLinks } from '../requester-paths';
import {
  REQUESTER_AUDIENCE,
  attachmentsEditable,
  isDraftStatus,
  requesterActionTone,
  requesterDetailBanner,
  requesterStatusHistory,
  requesterTrackerTone,
  serviceRequestActionLabelKey,
  serviceRequestStatusTone,
  serviceRequestStatusWording,
} from '../requester-status';
import { RequesterGates } from '../requester-permissions';
import { ServiceRequestsRequesterTranslationKeys } from '../requester-translation-keys';
import { ServiceRequestDetailStore } from './service-request-detail.store';

const NOT_FOUND: ApiError = Object.freeze({ status: HttpStatus.notFound, code: RecordsErrorCodes.NotFound, fieldErrors: [] });

/**
 * `/service-requests/:id` (requester shell, `service-requests.view-own`): one of the requester's requests on the
 * requester-detail pattern. The header shows the number and the status in the requester's wording; the main column the
 * last return or rejection reason, the request's fields (detail cards), its timeline and its attachments (editable
 * only while it is returned for changes); the aside the status tracker and the actions.
 *
 * The actions are EXACTLY the server's allowed actions for the requester (cancel, resubmit): the decision panel
 * renders nothing else, so a rejected request offers no resubmit and a terminal request offers nothing. A draft links
 * back to the wizard instead.
 */
@Component({
  selector: 'app-service-request-detail',
  imports: [
    DecisionPanelComponent,
    DgaAlertComponent,
    DgaButtonComponent,
    HasPermissionDirective,
    RecordAttachmentsComponent,
    RecordDetailCardsComponent,
    RecordTimelineComponent,
    RequesterDetailActionsDirective,
    RequesterDetailAttachmentsDirective,
    RequesterDetailPatternComponent,
    RouterLink,
    TranslatePipe,
  ],
  providers: [ServiceRequestDetailStore],
  templateUrl: './service-request-detail.component.html',
  styleUrl: './service-request-detail.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ServiceRequestDetailComponent implements OnInit {
  /** The request's PublicId (route parameter `id`). */
  readonly id = input<string | undefined>(undefined);

  protected readonly store = inject(ServiceRequestDetailStore);
  private readonly router = inject(Router);
  private readonly language = inject(LanguageService);

  protected readonly keys = ServiceRequestsRequesterTranslationKeys.Detail;
  protected readonly gates = RequesterGates;
  protected readonly audience = REQUESTER_AUDIENCE;
  protected readonly attachableType = SERVICE_REQUEST_ATTACHABLE_RECORD_TYPE;
  protected readonly translatedCodes = RECORDS_TRANSLATED_CODES;
  protected readonly statusKeyFor = serviceRequestStatusWording;
  protected readonly actionLabelKeyFor = serviceRequestActionLabelKey;
  protected readonly trackerTone = requesterTrackerTone;
  protected readonly actionTone = requesterActionTone;
  protected readonly links = serviceRequestsRequesterLinks(inject(SERVICE_REQUESTS_REQUESTER_BASE_LINK));
  private readonly parsedId = computed<ServiceRequestId | null>(() => serviceRequestIdOf(this.id()));

  readonly record = computed(() => this.store.view()?.record ?? null);
  readonly pageError = computed(() => (this.parsedId() === null ? NOT_FOUND : this.store.error()));
  readonly statusKey = computed(() => {
    const record = this.record();
    return record ? statusWordingKey(record.wording, REQUESTER_AUDIENCE) : null;
  });
  readonly statusTone = computed<DgaStatusTone>(() => serviceRequestStatusTone(this.record()?.statusKey ?? ''));
  readonly history = computed(() => requesterStatusHistory(this.store.view()?.timeline ?? []));
  readonly banner = computed(() => {
    const record = this.record();
    return record ? requesterDetailBanner(record) : null;
  });
  readonly cards = computed(() => {
    const details = this.store.view()?.details;
    return details ? serviceRequestCardsAdapter(details, this.language.current()) : [];
  });
  readonly isDraft = computed(() => {
    const record = this.record();
    return record !== null && isDraftStatus(record.status);
  });
  /** The record whose files the page lists: none for a draft (its files are attached in the wizard). */
  readonly attachmentsRecordId = computed(() => {
    const record = this.record();
    return record === null || isDraftStatus(record.status) ? null : record.id;
  });
  readonly attachmentsReadonly = computed(() => {
    const record = this.record();
    return record === null || !attachmentsEditable(record);
  });
  /** The panel shows only while the server offers the requester something to do. */
  readonly hasActions = computed(() => {
    const record = this.record();
    return record !== null && !record.isTerminal && record.allowedActions.length > 0;
  });

  ngOnInit(): void {
    const id = this.parsedId();
    if (id !== null) this.store.load(id);
  }

  decide(decision: RecordDecision): void {
    this.store.decide(decision);
  }

  continueDraft(): void {
    const id = this.parsedId();
    if (id !== null) void this.router.navigateByUrl(this.links.draft(id));
  }

  retry(): void {
    this.store.reload();
  }
}
