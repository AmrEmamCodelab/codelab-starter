import { ChangeDetectionStrategy, Component, computed, effect, inject, input, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DgaAlertComponent, DgaAlertVariant, DgaIconComponent, DgaPageFeedbackComponent } from '@codelab/ng-dga';
import { LanguageService } from '../../../core/i18n/language.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import {
  RequesterDetailAttachmentsDirective,
  RequesterDetailPatternComponent,
} from '../../../patterns';
import { RecordAttachmentsComponent } from '../../../shared/record-attachments/record-attachments.component';
import { RECORDS_TRANSLATED_CODES, RecordDetailCardsComponent, StatusWordingPipe, WorkflowAudience } from '../../../shared/records';
import { serviceRequestAttachmentsFeedback } from '../data';
import { serviceRequestDetailCards } from '../staff/service-request-record-card';
import { serviceRequestStatusTone } from '../staff/service-request-status-display';
import { adminHistory, adminHistoryTone } from './admin-history';
import { ServiceRequestAdminDetailStore } from './service-request-admin-detail.store';
import { ServiceRequestsAdminPaths } from './service-requests-admin.paths';
import { ServiceRequestsAdminTranslationKeys } from './service-requests-admin-translation-keys';

/** The detail cards sit below the page's h1 and the summary region. */
const CARD_HEADING_LEVEL = 2;

/**
 * The admin's read-only detail of one service request (`/admin/requests/:id`, `records.oversight.view`), on the
 * requester-detail pattern: number, created on and status in the ADMIN wording, the request's cards, its attachments
 * (download only) and the full history with actors and reasons. READ-ONLY: no decision panel, no claim and no action
 * group (the pattern renders none without a `[detailActions]` slot).
 */
@Component({
  selector: 'app-service-request-admin-detail-page',
  imports: [
    DgaAlertComponent,
    DgaIconComponent,
    DgaPageFeedbackComponent,
    RecordAttachmentsComponent,
    RecordDetailCardsComponent,
    RequesterDetailAttachmentsDirective,
    RequesterDetailPatternComponent,
    RouterLink,
    StatusWordingPipe,
    TranslatePipe,
  ],
  providers: [ServiceRequestAdminDetailStore],
  templateUrl: './service-request-admin-detail-page.component.html',
  styleUrl: './service-request-admin-detail-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ServiceRequestAdminDetailPageComponent {
  /** The route's `:id` (the request's PublicId). */
  readonly id = input.required<string>();

  protected readonly store = inject(ServiceRequestAdminDetailStore);
  private readonly language = inject(LanguageService);
  private readonly translations = inject(TranslationService);

  protected readonly keys = ServiceRequestsAdminTranslationKeys.Detail;
  protected readonly paths = ServiceRequestsAdminPaths;
  protected readonly audience = WorkflowAudience.Admin;
  protected readonly AlertVariant = DgaAlertVariant;
  protected readonly translatedCodes = RECORDS_TRANSLATED_CODES;
  protected readonly cardHeadingLevel = CARD_HEADING_LEVEL;
  protected readonly historyTone = adminHistoryTone;

  protected readonly history = computed(() => adminHistory(this.store.timeline()));
  protected readonly statusTone = computed(() => serviceRequestStatusTone(this.store.detail()?.status ?? 0));
  protected readonly cards = computed(() => {
    const detail = this.store.detail();
    const details = this.store.details();
    return detail === null || details === null ? [] : serviceRequestDetailCards(detail, details, this.language.current());
  });

  /** The error shown in place of the attachments when the record carried no attachments id. */
  protected readonly attachmentsFeedback = computed(() =>
    serviceRequestAttachmentsFeedback(this.store.attachmentsError(), (key, params) => this.translations.translate(key, params)),
  );

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => void this.store.open(id));
    });
  }
}
