import { ChangeDetectionStrategy, Component, inject, type OnInit } from '@angular/core';
import { DgaButtonComponent, DgaStatusTagComponent } from '@codelab/ng-dga';
import type { OpsCertificate } from '../../core/api/ops-api.models';
import { CommonKeys } from '../../core/i18n/common-translation-keys';
import { DateTimePipe } from '../../core/i18n/date-time';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import type { ColumnDef } from '../../patterns/ops-list/list-columns';
import { OpsListCellDirective } from '../../patterns/ops-list/ops-list-cell.directive';
import { OpsListComponent } from '../../patterns/ops-list/ops-list.component';
import { CertificateKeys, certificateStatusView } from '../../shared/certificates/certificate-status';
import { CertificatesStore } from '../../shared/certificates/certificates.store';

/** Certificates: every certificate the deployment depends on, soonest expiry first, with status badges. */
@Component({
  selector: 'app-certificates-page',
  imports: [
    DateTimePipe,
    DgaButtonComponent,
    DgaStatusTagComponent,
    OpsListCellDirective,
    OpsListComponent,
    TranslatePipe,
  ],
  templateUrl: './certificates.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CertificatesPage implements OnInit {
  protected readonly store = inject(CertificatesStore);
  protected readonly keys = CertificateKeys;
  protected readonly common = CommonKeys;
  protected readonly statusView = certificateStatusView;
  protected readonly columns: readonly ColumnDef<OpsCertificate>[] = [
    { key: 'name', headerKey: CertificateKeys.Columns.Name, ltr: true },
    { key: 'subject', headerKey: CertificateKeys.Columns.Subject, ltr: true },
    { key: 'notAfter', headerKey: CertificateKeys.Columns.NotAfter },
    { key: 'daysLeft', headerKey: CertificateKeys.Columns.DaysLeft },
    { key: 'status', headerKey: CertificateKeys.Columns.Status },
  ];
  protected readonly rowId = (certificate: OpsCertificate): string => certificate.name;

  ngOnInit(): void {
    this.store.load();
  }
}
