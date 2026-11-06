import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DgaButtonComponent, DgaStatusTagComponent } from '@codelab/ng-dga';
import { apiErrorMessageKey } from '@codelab/ng-security';
import { LanguageService } from '../../../../core/i18n/language.service';
import { TranslatePipe } from '../../../../core/i18n/translate.pipe';
import { PermissionStore } from '../../../../core/permissions/permission.store';
import { REQUESTER_SHELL_OPTIONS } from '../../../../layouts/requester-shell/requester-nav';
import { LandingDashboardPatternComponent } from '../../../../patterns';
import { RECORDS_TRANSLATED_CODES, formatRecordInstant, type RecordListItem } from '../../../../shared/records';
import { SERVICE_REQUESTS_REQUESTER_BASE_LINK, serviceRequestsRequesterLinks } from '../requester-paths';
import { requesterStatusKey, serviceRequestStatusTone } from '../requester-status';
import { ServiceRequestsRequesterTranslationKeys } from '../requester-translation-keys';
import { requestLink, requesterKpiTiles, requesterQuickActions } from './requester-dashboard';
import { RequesterDashboardStore } from './requester-dashboard.store';

/**
 * `/service-requests` (requester shell, `service-requests.view-own`): the requester's landing dashboard on the
 * landing-dashboard pattern. KPI tiles count the requester's requests by status, the quick actions start a request,
 * open my requests or the notifications feed (each shown only to a user who may follow it), and the recent list shows
 * the newest requests in the requester's status wording.
 */
@Component({
  selector: 'app-service-requests-requester-dashboard',
  imports: [DgaButtonComponent, DgaStatusTagComponent, LandingDashboardPatternComponent, RouterLink, TranslatePipe],
  providers: [RequesterDashboardStore],
  templateUrl: './requester-dashboard.component.html',
  styleUrl: './requester-dashboard.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RequesterDashboardComponent {
  protected readonly store = inject(RequesterDashboardStore);
  private readonly permissions = inject(PermissionStore);
  private readonly language = inject(LanguageService);
  private readonly notificationsLink = inject(REQUESTER_SHELL_OPTIONS).notificationsLink;

  protected readonly keys = ServiceRequestsRequesterTranslationKeys.Dashboard;
  protected readonly links = serviceRequestsRequesterLinks(inject(SERVICE_REQUESTS_REQUESTER_BASE_LINK));

  readonly tiles = computed(() => requesterKpiTiles(this.store.counts(), this.links.mine));
  readonly quickActions = computed(() =>
    requesterQuickActions({
      links: this.links,
      has: (key) => this.permissions.has(key),
      notificationsLink: this.notificationsLink,
    }),
  );
  readonly errorMessageKey = computed(() => {
    const error = this.store.error();
    return error ? apiErrorMessageKey(error, RECORDS_TRANSLATED_CODES) : null;
  });

  constructor() {
    this.store.load();
  }

  linkOf(item: RecordListItem): string {
    return requestLink(item, this.links);
  }

  statusKeyOf(item: RecordListItem): string {
    return requesterStatusKey(item.statusKey);
  }

  toneOf(item: RecordListItem): ReturnType<typeof serviceRequestStatusTone> {
    return serviceRequestStatusTone(item.statusKey);
  }

  createdText(item: RecordListItem): string {
    return formatRecordInstant(item.createdAt, this.language.current());
  }

  retry(): void {
    this.store.load();
  }
}
