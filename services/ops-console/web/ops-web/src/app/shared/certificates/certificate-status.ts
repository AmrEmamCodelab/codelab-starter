import type { DgaStatusTone } from '@codelab/ng-dga';
import { CertificateStatus, type OpsCertificate } from '../../core/api/ops-api.models';

/** Translation keys of the certificate screens and the certificate alert banner. */
export const CertificateKeys = {
  Title: 'certificates.title',
  Subtitle: 'certificates.subtitle',
  Caption: 'certificates.caption',
  Empty: 'certificates.empty',
  Columns: {
    Name: 'certificates.columns.name',
    Subject: 'certificates.columns.subject',
    NotAfter: 'certificates.columns.notAfter',
    DaysLeft: 'certificates.columns.daysLeft',
    Status: 'certificates.columns.status',
  },
  Status: {
    Ok: 'certificates.status.ok',
    Warning: 'certificates.status.warning',
    Critical: 'certificates.status.critical',
    Expired: 'certificates.status.expired',
    Unknown: 'certificates.status.unknown',
  },
  Alert: {
    IdpTitle: 'certificates.alert.idpTitle',
    AnyTitle: 'certificates.alert.anyTitle',
    /** `{{name}}`, `{{days}}`. */
    ExpiresIn: 'certificates.alert.expiresIn',
    /** `{{name}}`. */
    Expired: 'certificates.alert.expired',
    Open: 'certificates.alert.open',
  },
} as const;

/** How a certificate status renders: the status-tag tone and its label key. */
export interface CertificateStatusView {
  readonly tone: DgaStatusTone;
  readonly labelKey: string;
}

const STATUS_VIEWS: ReadonlyMap<CertificateStatus, CertificateStatusView> = new Map([
  [CertificateStatus.Ok, { tone: 'success', labelKey: CertificateKeys.Status.Ok }],
  [CertificateStatus.Warning, { tone: 'warning', labelKey: CertificateKeys.Status.Warning }],
  [CertificateStatus.Critical, { tone: 'error', labelKey: CertificateKeys.Status.Critical }],
  [CertificateStatus.Expired, { tone: 'error', labelKey: CertificateKeys.Status.Expired }],
]);

const UNKNOWN_STATUS_VIEW: CertificateStatusView = Object.freeze({ tone: 'neutral', labelKey: CertificateKeys.Status.Unknown });

/** The tone and label of a status; a value this build does not know renders neutral as "unknown", never as Ok. */
export function certificateStatusView(status: CertificateStatus): CertificateStatusView {
  return STATUS_VIEWS.get(status) ?? UNKNOWN_STATUS_VIEW;
}

/** True for Warning, Critical and Expired: the statuses the alert banner reports. */
export function needsAttention(status: CertificateStatus): boolean {
  return status >= CertificateStatus.Warning;
}

/** The order the list shows: soonest expiry first, then by name. */
export function bySoonestExpiry(left: OpsCertificate, right: OpsCertificate): number {
  return left.daysLeft - right.daysLeft || left.name.localeCompare(right.name);
}

/** What the alert banner shows: the certificates that need attention, soonest first, and whether they are the IdP's. */
export interface CertificateAlert {
  readonly idp: boolean;
  readonly certificates: readonly OpsCertificate[];
}

/**
 * The alert banner's content, or null when there is nothing to report. With `idpCertificateName` set, only that
 * certificate is watched (the IdP signing certificate: login stops when it expires); without it, every certificate is.
 */
export function certificateAlert(
  certificates: readonly OpsCertificate[],
  idpCertificateName: string | null,
): CertificateAlert | null {
  const watched = idpCertificateName === null
    ? certificates
    : certificates.filter((certificate) => certificate.name === idpCertificateName);
  const attention = watched.filter((certificate) => needsAttention(certificate.status)).sort(bySoonestExpiry);
  return attention.length === 0 ? null : { idp: idpCertificateName !== null, certificates: attention };
}

/** The banner tone: an error alert once any reported certificate is Critical or Expired, a warning before that. */
export function certificateAlertVariant(alert: CertificateAlert): 'warning' | 'error' {
  return alert.certificates.some((certificate) => certificate.status >= CertificateStatus.Critical) ? 'error' : 'warning';
}

/** One line of the banner: the certificate, the message key and its parameters. */
export interface CertificateAlertLine {
  readonly name: string;
  readonly key: string;
  readonly params: Readonly<Record<string, string | number>>;
}

/** The banner lines: "expired" for an expired certificate, "expires in N days" otherwise. */
export function certificateAlertLines(alert: CertificateAlert): readonly CertificateAlertLine[] {
  return alert.certificates.map((certificate): CertificateAlertLine =>
    certificate.status === CertificateStatus.Expired
      ? { name: certificate.name, key: CertificateKeys.Alert.Expired, params: { name: certificate.name } }
      : { name: certificate.name, key: CertificateKeys.Alert.ExpiresIn, params: { name: certificate.name, days: certificate.daysLeft } });
}
