import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { OpsApiClient } from '../../core/api/ops-api.client';
import { CertificateStatus, type OpsCertificate } from '../../core/api/ops-api.models';
import { FakeOpsApiClient, opsTestProviders, textOf } from '../../core/testing/ops.testing';
import { CertificateAlertComponent } from './certificate-alert.component';
import {
  CertificateKeys,
  bySoonestExpiry,
  certificateAlert,
  certificateAlertLines,
  certificateAlertVariant,
  certificateStatusView,
  needsAttention,
} from './certificate-status';
import { CertificatesStore } from './certificates.store';

function certificate(name: string, status: CertificateStatus, daysLeft: number): OpsCertificate {
  return { name, subject: `CN=${name}`, notAfter: '2026-12-01T00:00:00Z', daysLeft, status };
}

const OK = certificate('api-tls', CertificateStatus.Ok, 200);
const WARNING = certificate('idp-signing', CertificateStatus.Warning, 21);
const CRITICAL = certificate('gateway-tls', CertificateStatus.Critical, 5);
const EXPIRED = certificate('old-tls', CertificateStatus.Expired, -3);

describe('certificate status', () => {
  it('maps every status to a tone and label, and an unknown value to neutral', () => {
    expect(certificateStatusView(CertificateStatus.Ok)).toEqual({ tone: 'success', labelKey: CertificateKeys.Status.Ok });
    expect(certificateStatusView(CertificateStatus.Warning).tone).toBe('warning');
    expect(certificateStatusView(CertificateStatus.Critical).tone).toBe('error');
    expect(certificateStatusView(CertificateStatus.Expired).labelKey).toBe(CertificateKeys.Status.Expired);
    expect(certificateStatusView(99 as CertificateStatus)).toEqual({ tone: 'neutral', labelKey: CertificateKeys.Status.Unknown });
  });

  it('needs attention from Warning on', () => {
    expect(needsAttention(CertificateStatus.Ok)).toBe(false);
    expect(needsAttention(CertificateStatus.Warning)).toBe(true);
    expect(needsAttention(CertificateStatus.Expired)).toBe(true);
  });

  it('sorts soonest expiry first, then by name', () => {
    const twin = certificate('a-tls', CertificateStatus.Ok, 200);
    expect([OK, WARNING, EXPIRED, twin].sort(bySoonestExpiry).map((row) => row.name)).toEqual(['old-tls', 'idp-signing', 'a-tls', 'api-tls']);
  });
});

describe('certificateAlert', () => {
  it('is null when every certificate is OK', () => {
    expect(certificateAlert([OK], null)).toBeNull();
  });

  it('reports every certificate at Warning or worse when no IdP certificate is configured', () => {
    expect(certificateAlert([OK, WARNING, CRITICAL], null)).toEqual({ idp: false, certificates: [CRITICAL, WARNING] });
  });

  it('watches only the configured IdP certificate', () => {
    expect(certificateAlert([CRITICAL, WARNING], 'idp-signing')).toEqual({ idp: true, certificates: [WARNING] });
    expect(certificateAlert([CRITICAL, OK], 'api-tls')).toBeNull();
  });

  it('is a warning until a reported certificate is Critical or Expired', () => {
    expect(certificateAlertVariant({ idp: false, certificates: [WARNING] })).toBe('warning');
    expect(certificateAlertVariant({ idp: false, certificates: [WARNING, CRITICAL] })).toBe('error');
    expect(certificateAlertVariant({ idp: false, certificates: [EXPIRED] })).toBe('error');
  });

  it('words expired and expiring certificates differently', () => {
    expect(certificateAlertLines({ idp: false, certificates: [EXPIRED, WARNING] })).toEqual([
      { name: 'old-tls', key: CertificateKeys.Alert.Expired, params: { name: 'old-tls' } },
      { name: 'idp-signing', key: CertificateKeys.Alert.ExpiresIn, params: { name: 'idp-signing', days: 21 } },
    ]);
  });
});

describe('CertificatesStore', () => {
  let client: FakeOpsApiClient;

  beforeEach(() => {
    client = new FakeOpsApiClient();
    TestBed.configureTestingModule({
      providers: [...opsTestProviders({ idpCertificateName: 'idp-signing' }), { provide: OpsApiClient, useValue: client }],
    });
  });

  it('loads, sorts and derives the IdP alert', () => {
    const store = TestBed.inject(CertificatesStore);
    store.load();
    expect(store.loading()).toBe(true);
    client.answer('certificates', [OK, CRITICAL, WARNING]);
    expect(store.rows().map((row) => row.name)).toEqual(['gateway-tls', 'idp-signing', 'api-tls']);
    expect(store.alert()).toEqual({ idp: true, certificates: [WARNING] });
  });

  it('exposes a failure and no alert', () => {
    const store = TestBed.inject(CertificatesStore);
    store.load();
    client.fail('certificates', 503, { code: 'RETRYABLE' });
    expect(store.error()?.status).toBe(503);
    expect(store.alert()).toBeNull();
  });
});

describe('CertificateAlertComponent', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: opsTestProviders() }));

  it('renders nothing without an alert', () => {
    const fixture = TestBed.createComponent(CertificateAlertComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="certificate-alert"]')).toBeNull();
  });

  it('renders the IdP title, one line per certificate and the link', () => {
    const fixture = TestBed.createComponent(CertificateAlertComponent);
    fixture.componentRef.setInput('alert', { idp: true, certificates: [WARNING] });
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain(CertificateKeys.Alert.IdpTitle);
    expect(textOf(root, 'li')).toBe(`${CertificateKeys.Alert.ExpiresIn}|name=idp-signing,days=21`);
    expect(root.querySelector('a')?.getAttribute('href')).toBe('/certificates');
    expect(fixture.componentInstance.variant()).toBe('warning');
  });

  it('can hide the link', () => {
    const fixture = TestBed.createComponent(CertificateAlertComponent);
    fixture.componentRef.setInput('alert', { idp: false, certificates: [EXPIRED] });
    fixture.componentRef.setInput('showLink', false);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('a')).toBeNull();
    expect(root.textContent).toContain(CertificateKeys.Alert.AnyTitle);
    expect(fixture.componentInstance.variant()).toBe('error');
  });
});
