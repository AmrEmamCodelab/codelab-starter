import { TestBed } from '@angular/core/testing';
import { HttpStatus } from '@codelab/ng-security';
import { Subject, of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { problem } from '../organizations/admin.testing';
import { AdminJobsClient } from './admin-jobs.client';
import { JobRunsPageStore } from './job-runs-page.store';
import { JobRunStatus, type JobRunAccepted, type JobSummary } from './job.models';
import { JobsPageStore } from './jobs-page.store';
import { RUN_POLL_INTERVAL_MS } from './run-poller';
import { catalog, jobRun, jobSummary } from './jobs.testing';

type FakeClient = Record<'list' | 'run' | 'runs', ReturnType<typeof vi.fn>>;

function fakeClient(): FakeClient {
  return {
    list: vi.fn().mockReturnValue(of(catalog())),
    run: vi.fn().mockReturnValue(of({ runId: 'run-2' })),
    runs: vi.fn().mockReturnValue(of([jobRun()])),
  };
}

function configure(client: FakeClient): void {
  TestBed.configureTestingModule({
    providers: [JobsPageStore, JobRunsPageStore, { provide: AdminJobsClient, useValue: client }],
  });
}

describe('JobRunTrigger (through JobsPageStore)', () => {
  let client: FakeClient;
  let store: JobsPageStore;

  beforeEach(() => {
    client = fakeClient();
    configure(client);
    store = TestBed.inject(JobsPageStore);
  });

  it('asks, then runs the pending job and answers its key', async () => {
    store.runTrigger.ask(jobSummary());
    expect(store.runTrigger.pending()?.key).toBe('outbox-drain');
    await expect(store.confirmRun()).resolves.toBe('outbox-drain');
    expect(client.run).toHaveBeenCalledWith('outbox-drain');
    expect(store.runTrigger.pending()).toBeNull();
  });

  it('keeps the dialog open with the refusal', async () => {
    client.run.mockReturnValue(throwError(() => problem(HttpStatus.notFound, 'NOT_FOUND')));
    store.runTrigger.ask(jobSummary());
    await expect(store.confirmRun()).resolves.toBeNull();
    expect(store.runTrigger.pending()).not.toBeNull();
    expect(store.runTrigger.error()?.code).toBe('NOT_FOUND');
    store.runTrigger.ask(jobSummary());
    expect(store.runTrigger.error()).toBeNull();
  });

  it('does nothing without a pending job, and cannot be cancelled while busy', async () => {
    await expect(store.confirmRun()).resolves.toBeNull();
    expect(client.run).not.toHaveBeenCalled();
    const pending = new Subject<JobRunAccepted>();
    client.run.mockReturnValue(pending);
    store.runTrigger.ask(jobSummary());
    const running = store.confirmRun();
    expect(store.runTrigger.busy()).toBe(true);
    store.runTrigger.cancel();
    await expect(store.confirmRun()).resolves.toBeNull();
    expect(store.runTrigger.pending()).not.toBeNull();
    pending.next({ runId: 'run-3' } as JobRunAccepted);
    pending.complete();
    await running;
    store.runTrigger.ask(jobSummary());
    store.runTrigger.cancel();
    expect(store.runTrigger.pending()).toBeNull();
  });
});

describe('JobsPageStore', () => {
  let client: FakeClient;
  let store: JobsPageStore;

  beforeEach(() => {
    vi.useFakeTimers();
    client = fakeClient();
    configure(client);
    store = TestBed.inject(JobsPageStore);
  });

  afterEach(() => vi.useRealTimers());

  it('loads the catalog', async () => {
    await store.refresh();
    expect(store.rows().map((job) => job.key)).toEqual(['outbox-drain', 'security-replay-purge']);
    expect(store.loading()).toBe(false);
  });

  it('reloads by itself while a last run is unfinished', async () => {
    client.list.mockReturnValueOnce(of([jobSummary({ lastRun: jobRun({ status: JobRunStatus.Running }) })]));
    await store.refresh();
    expect(client.list).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(RUN_POLL_INTERVAL_MS);
    expect(client.list).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(RUN_POLL_INTERVAL_MS);
    expect(client.list).toHaveBeenCalledTimes(2);
  });

  it('clears the rows and keeps the refusal when the load fails', async () => {
    client.list.mockReturnValue(throwError(() => problem(HttpStatus.serverError, 'INTERNAL_ERROR')));
    await store.refresh();
    expect(store.rows()).toEqual([]);
    expect(store.error()?.code).toBe('INTERNAL_ERROR');
  });

  it('pages the catalog on the client', async () => {
    const many: JobSummary[] = Array.from({ length: store.pageSize + 1 }, (_, index) => jobSummary({ key: `job-${index}`, lastRun: null }));
    client.list.mockReturnValue(of(many));
    await store.refresh();
    expect(store.rows()).toHaveLength(store.pageSize);
    store.page.set(2);
    expect(store.rows().map((job) => job.key)).toEqual([`job-${store.pageSize}`]);
  });
});

describe('JobRunsPageStore', () => {
  let client: FakeClient;
  let store: JobRunsPageStore;

  beforeEach(() => {
    vi.useFakeTimers();
    client = fakeClient();
    configure(client);
    store = TestBed.inject(JobRunsPageStore);
  });

  afterEach(() => vi.useRealTimers());

  it('does nothing before a job is opened', async () => {
    await store.refresh();
    expect(client.runs).not.toHaveBeenCalled();
  });

  it('opens a job: its catalog entry and its runs', async () => {
    await store.open('outbox-drain');
    expect(store.jobKey()).toBe('outbox-drain');
    expect(store.job()?.nameEn).toBe('Outbox drain');
    expect(store.runs()).toEqual([jobRun()]);
    expect(client.runs).toHaveBeenCalledWith('outbox-drain');
  });

  it('keeps the job null when the catalog no longer lists it', async () => {
    await store.open('gone');
    expect(store.job()).toBeNull();
    store.askRun();
    expect(store.runTrigger.pending()).toBeNull();
  });

  it('shows the refusal for an unknown key', async () => {
    client.runs.mockReturnValue(throwError(() => problem(HttpStatus.notFound, 'NOT_FOUND')));
    await store.open('gone');
    expect(store.runs()).toEqual([]);
    expect(store.error()?.code).toBe('NOT_FOUND');
  });

  it('runs the job, reloads and follows the new run until it finishes', async () => {
    await store.open('outbox-drain');
    client.runs
      .mockReturnValueOnce(of([jobRun({ id: 'run-2' as never, status: JobRunStatus.Queued, trigger: 2, triggeredBy: 'admin' }), jobRun()]))
      .mockReturnValueOnce(of([jobRun({ id: 'run-2' as never, status: JobRunStatus.Succeeded }), jobRun()]));
    store.askRun();
    await expect(store.confirmRun()).resolves.toEqual({ runId: 'run-2' });
    expect(store.runs()[0].status).toBe(JobRunStatus.Queued);
    await vi.advanceTimersByTimeAsync(RUN_POLL_INTERVAL_MS);
    expect(store.runs()[0].status).toBe(JobRunStatus.Succeeded);
    const calls = client.runs.mock.calls.length;
    await vi.advanceTimersByTimeAsync(RUN_POLL_INTERVAL_MS);
    expect(client.runs.mock.calls.length).toBe(calls);
  });

  it('does not reload when the run is refused', async () => {
    await store.open('outbox-drain');
    client.run.mockReturnValue(throwError(() => problem(HttpStatus.forbidden, 'FORBIDDEN')));
    store.askRun();
    await expect(store.confirmRun()).resolves.toBeNull();
    expect(client.runs).toHaveBeenCalledTimes(1);
  });
});
