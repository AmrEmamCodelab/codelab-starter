import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { DgaButtonComponent, DgaSelectComponent, DgaStatusTagComponent, type DgaStatusTone } from '@codelab/ng-dga';
import { LanguageService } from '../../../../core/i18n/language.service';
import { TranslatePipe } from '../../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../../core/i18n/translation.service';
import { AdminListPatternComponent, FIRST_PAGE, ListCellDirective, ListRowActionsDirective } from '../../../../patterns';
import { HasPermissionDirective } from '../../../../shared/permissions/has-permission.directive';
import { RECORDS_TRANSLATED_CODES, formatRecordInstant, type RecordListItem } from '../../../../shared/records';
import { type ServiceRequestStatus } from '../../data';
import { requestLink } from '../dashboard/requester-dashboard';
import { SERVICE_REQUESTS_REQUESTER_BASE_LINK, serviceRequestsRequesterLinks } from '../requester-paths';
import { isDraftStatus, requesterStatusKey, serviceRequestStatusTone } from '../requester-status';
import { RequesterGates } from '../requester-permissions';
import { ServiceRequestsRequesterTranslationKeys } from '../requester-translation-keys';
import { MY_REQUESTS_COLUMNS, MyRequestsColumns, myRequestRowId, statusFilterOptions } from './my-requests';
import { MyRequestsStore } from './my-requests.store';

/**
 * `/service-requests/mine` (requester shell, `service-requests.view-own`): the requester's own requests on the
 * admin-list pattern, one page at a time, with a status filter. Each row shows the number (none until submitted), the
 * status in the requester's wording, and links to the detail page, or back into the wizard for a draft.
 */
@Component({
  selector: 'app-service-requests-my-requests',
  imports: [
    AdminListPatternComponent,
    DgaButtonComponent,
    DgaSelectComponent,
    DgaStatusTagComponent,
    HasPermissionDirective,
    ListCellDirective,
    ListRowActionsDirective,
    RouterLink,
    TranslatePipe,
  ],
  providers: [MyRequestsStore],
  templateUrl: './my-requests.component.html',
  styleUrl: './my-requests.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyRequestsComponent {
  protected readonly store = inject(MyRequestsStore);
  private readonly router = inject(Router);
  private readonly language = inject(LanguageService);
  private readonly translations = inject(TranslationService);

  protected readonly keys = ServiceRequestsRequesterTranslationKeys.Mine;
  protected readonly gates = RequesterGates;
  protected readonly columns = MY_REQUESTS_COLUMNS;
  protected readonly columnKeys = MyRequestsColumns;
  protected readonly rowId = myRequestRowId;
  protected readonly translatedCodes = RECORDS_TRANSLATED_CODES;
  protected readonly links = serviceRequestsRequesterLinks(inject(SERVICE_REQUESTS_REQUESTER_BASE_LINK));

  readonly page = signal(FIRST_PAGE);
  readonly status = signal<ServiceRequestStatus | null>(null);
  readonly statusOptions = computed(() => statusFilterOptions((key) => this.translations.translate(key)));

  constructor() {
    effect(() => {
      const query = { page: this.page(), status: this.status() };
      untracked(() => this.store.load(query));
    });
  }

  /** A new filter starts again at the first page. */
  filterBy(status: ServiceRequestStatus | null): void {
    this.status.set(status);
    this.page.set(FIRST_PAGE);
  }

  createRequest(): void {
    void this.router.navigateByUrl(this.links.create);
  }

  linkOf(row: RecordListItem): string {
    return requestLink(row, this.links);
  }

  isDraft(row: RecordListItem): boolean {
    return isDraftStatus(row.status);
  }

  statusKeyOf(row: RecordListItem): string {
    return requesterStatusKey(row.statusKey);
  }

  toneOf(row: RecordListItem): DgaStatusTone {
    return serviceRequestStatusTone(row.statusKey);
  }

  instantText(iso: string | null): string {
    return iso === null ? '' : formatRecordInstant(iso, this.language.current());
  }
}
