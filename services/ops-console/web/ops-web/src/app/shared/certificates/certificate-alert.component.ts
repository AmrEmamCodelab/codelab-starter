import { ChangeDetectionStrategy, Component, booleanAttribute, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DgaAlertComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { RoutePaths } from '../../core/routing/route-paths';
import {
  CertificateKeys,
  certificateAlertLines,
  certificateAlertVariant,
  type CertificateAlert,
  type CertificateAlertLine,
} from './certificate-status';

const NO_LINES: readonly CertificateAlertLine[] = Object.freeze([]);

/**
 * The certificate expiry banner (the IdP signing certificate, or every certificate when none is configured): a warning
 * alert while the reported certificates are at Warning, an error alert from Critical on. Renders nothing for null.
 */
@Component({
  selector: 'app-certificate-alert',
  imports: [DgaAlertComponent, RouterLink, TranslatePipe],
  templateUrl: './certificate-alert.component.html',
  styleUrl: './certificate-alert.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CertificateAlertComponent {
  readonly alert = input<CertificateAlert | null>(null);
  /** Show the link to the Certificates screen (off on that screen itself). */
  readonly showLink = input(true, { transform: booleanAttribute });

  protected readonly keys = CertificateKeys;
  protected readonly certificatesLink = `/${RoutePaths.Certificates}`;

  readonly variant = computed(() => {
    const alert = this.alert();
    return alert ? certificateAlertVariant(alert) : 'warning';
  });
  readonly titleKey = computed(() => (this.alert()?.idp ? CertificateKeys.Alert.IdpTitle : CertificateKeys.Alert.AnyTitle));
  readonly lines = computed(() => {
    const alert = this.alert();
    return alert ? certificateAlertLines(alert) : NO_LINES;
  });
}
