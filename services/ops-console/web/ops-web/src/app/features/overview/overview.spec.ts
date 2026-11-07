import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { OpsApiClient } from '../../core/api/ops-api.client';
import { OpsHealthStatus, type OpsHealthCheck, type OpsHealthReport } from '../../core/api/ops-api.models';
import { FakeOpsApiClient, opsTestProviders, textOf } from '../../core/testing/ops.testing';
import { OverviewKeys, byWorstFirst, healthStatusView } from './health-status';
import { HealthStore } from './health.store';
import { OverviewPage } from './overview.page';

function check(name: string, status: OpsHealthStatus, description: string | null = null): OpsHealthCheck {
  return { name, status, durationMs: 12, description };
}

const REPORT: OpsHealthReport = {
  status: OpsHealthStatus.Degraded,
  checkedAt: '2026-11-07T08:00:00Z',
  checks: [check('sql', OpsHealthStatus.Healthy), check('seq', OpsHealthStatus.Degraded, 'slow'), check('gateway', OpsHealthStatus.Unhealthy, 'down')],
};

describe('health status', () => {
  it('maps statuses to tones and an unknown value to neutral', () => {
    expect(healthStatusView(OpsHealthStatus.Healthy)).toEqual({ tone: 'success', labelKey: OverviewKeys.Status.Healthy });
    expect(healthStatusView(OpsHealthStatus.Degraded).tone).toBe('warning');
    expect(healthStatusView(OpsHealthStatus.Unhealthy).tone).toBe('error');
    expect(healthStatusView(0 as OpsHealthStatus).labelKey).toBe(OverviewKeys.Status.Unknown);
  });

  it('orders worst first, then by name', () => {
    const rows = [check('b', OpsHealthStatus.Healthy), check('a', OpsHealthStatus.Healthy), check('z', OpsHealthStatus.Unhealthy)];
    expect(rows.sort(byWorstFirst).map((row) => row.name)).toEqual(['z', 'a', 'b']);
  });
});

describe('Overview', () => {
  let client: FakeOpsApiClient;

  beforeEach(() => {
    client = new FakeOpsApiClient();
    TestBed.configureTestingModule({ providers: [...opsTestProviders(), { provide: OpsApiClient, useValue: client }] });
  });

  it('the store sorts checks and derives the overall view', () => {
    TestBed.configureTestingModule({ providers: [HealthStore] });
    const store = TestBed.inject(HealthStore);
    expect(store.overall()).toBeNull();
    store.load();
    client.answer('health', REPORT);
    expect(store.checks().map((row) => row.name)).toEqual(['gateway', 'seq', 'sql']);
    expect(store.overall()?.labelKey).toBe(OverviewKeys.Status.Degraded);
  });

  it('the page loads on open, renders the checks and refreshes on demand', () => {
    const fixture = TestBed.createComponent(OverviewPage);
    fixture.detectChanges();
    expect(client.calls).toEqual(['health']);
    client.answer('health', REPORT);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(textOf(root, '[data-testid="overall-health"] dga-status-tag')).toBe(OverviewKeys.Status.Degraded);
    expect(root.querySelectorAll('tbody tr')).toHaveLength(REPORT.checks.length);
    expect(textOf(root, 'tbody tr td')).toBe('gateway');
    expect(root.textContent).toContain(`${OverviewKeys.Duration}|count=12`);
    root.querySelector<HTMLButtonElement>('.ops-page__header dga-button button')?.click();
    expect(client.calls).toEqual(['health', 'health']);
  });

  it('shows a failure with its reference code', () => {
    const fixture = TestBed.createComponent(OverviewPage);
    fixture.detectChanges();
    client.fail('health', 503, { code: 'RETRYABLE', correlationId: '7d2c1e9a04b65f38c1a9e2d70b4f6a15' });
    fixture.detectChanges();
    expect(textOf(fixture.nativeElement, '[data-testid="error-reference-code"]')).toContain('7D2C1-E9A04');
  });
});
