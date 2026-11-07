import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { OpsApiClient } from '../../core/api/ops-api.client';
import { JobRunStatus, type OpsJob } from '../../core/api/ops-api.models';
import { CommonKeys } from '../../core/i18n/common-translation-keys';
import { FakeOpsApiClient, opsTestProviders, textOf } from '../../core/testing/ops.testing';
import { JobKeys, byFailedFirst, jobStatusView } from './job-status';
import { JobsPage } from './jobs.page';
import { JobsStore } from './jobs.store';

function job(key: string, lastStatus: JobRunStatus | null): OpsJob {
  return { key, lastStatus, lastStartedAt: null, lastFinishedAt: null, nextRunAt: '2026-11-07T09:00:00Z' };
}

describe('job status', () => {
  it('maps every run status, never-run and unknown values', () => {
    expect(jobStatusView(JobRunStatus.Queued).tone).toBe('info');
    expect(jobStatusView(JobRunStatus.Running).tone).toBe('progress');
    expect(jobStatusView(JobRunStatus.Succeeded)).toEqual({ tone: 'success', labelKey: JobKeys.Status.Succeeded });
    expect(jobStatusView(JobRunStatus.Failed).tone).toBe('error');
    expect(jobStatusView(null).labelKey).toBe(JobKeys.Status.NeverRun);
    expect(jobStatusView(9 as JobRunStatus).labelKey).toBe(JobKeys.Status.Unknown);
  });

  it('orders failed jobs first, then by key', () => {
    const rows = [job('b', JobRunStatus.Succeeded), job('z', JobRunStatus.Failed), job('a', null), job('y', JobRunStatus.Failed)];
    expect(rows.sort(byFailedFirst).map((row) => row.key)).toEqual(['y', 'z', 'a', 'b']);
  });
});

describe('Jobs', () => {
  let client: FakeOpsApiClient;

  beforeEach(() => {
    client = new FakeOpsApiClient();
    TestBed.configureTestingModule({ providers: [...opsTestProviders(), { provide: OpsApiClient, useValue: client }, JobsStore] });
  });

  it('the store counts failed jobs', () => {
    const store = TestBed.inject(JobsStore);
    expect(store.loaded()).toBe(false);
    store.load();
    client.answer('jobs', [job('outbox-drain', JobRunStatus.Failed), job('cleanup', JobRunStatus.Succeeded)]);
    expect(store.loaded()).toBe(true);
    expect(store.failedCount()).toBe(1);
    expect(store.rows()[0]?.key).toBe('outbox-drain');
  });

  it('the page shows the failed summary and badges', () => {
    const fixture = TestBed.createComponent(JobsPage);
    fixture.detectChanges();
    client.answer('jobs', [job('outbox-drain', JobRunStatus.Failed), job('cleanup', null)]);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(textOf(root, '[data-testid="jobs-failed"]')).toContain(`${JobKeys.FailedSummary}|count=1`);
    expect([...root.querySelectorAll('dga-status-tag')].map((tag) => tag.textContent?.trim())).toEqual([
      JobKeys.Status.Failed,
      JobKeys.Status.NeverRun,
    ]);
    expect(root.textContent).toContain(CommonKeys.NotAvailable);
  });

  it('the page says all good when nothing failed', () => {
    const fixture = TestBed.createComponent(JobsPage);
    fixture.detectChanges();
    client.answer('jobs', [job('cleanup', JobRunStatus.Succeeded)]);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(JobKeys.AllGood);
  });
});
