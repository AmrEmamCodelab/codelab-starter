import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { OpsApiClient } from '../../core/api/ops-api.client';
import { CertificateStatus } from '../../core/api/ops-api.models';
import { FakeOpsApiClient, opsTestProviders } from '../../core/testing/ops.testing';
import { CertificateKeys } from '../../shared/certificates/certificate-status';
import { CertificatesPage } from './certificates.page';

describe('CertificatesPage', () => {
  let client: FakeOpsApiClient;

  beforeEach(() => {
    client = new FakeOpsApiClient();
    TestBed.configureTestingModule({ providers: [...opsTestProviders(), { provide: OpsApiClient, useValue: client }] });
  });

  it('loads on open and shows a badge per status, soonest expiry first', () => {
    const fixture = TestBed.createComponent(CertificatesPage);
    fixture.detectChanges();
    expect(client.calls).toEqual(['certificates']);
    client.answer('certificates', [
      { name: 'api-tls', subject: 'CN=api', notAfter: '2027-06-01T00:00:00Z', daysLeft: 200, status: CertificateStatus.Ok },
      { name: 'idp-signing', subject: 'CN=idp', notAfter: '2026-11-20T00:00:00Z', daysLeft: 13, status: CertificateStatus.Critical },
    ]);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect([...root.querySelectorAll('dga-status-tag')].map((tag) => tag.textContent?.trim())).toEqual([
      CertificateKeys.Status.Critical,
      CertificateKeys.Status.Ok,
    ]);
    expect(root.querySelector('tbody td')?.textContent?.trim()).toBe('idp-signing');
  });
});
