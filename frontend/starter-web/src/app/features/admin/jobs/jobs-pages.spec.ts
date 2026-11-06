import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { Router } from '@angular/router';
import { HttpStatus } from '@codelab/ng-security';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { adminTestProviders, allByTestId, byTestId, grant, press, problem, settle } from '../organizations/admin.testing';
import { AdminJobsClient } from './admin-jobs.client';
import { JobRunsPageComponent } from './job-runs-page.component';
import { JobRunStatus, JobTrigger } from './job.models';
import { JobsPageComponent } from './jobs-page.component';
import { JobsPermissionKeys } from './jobs-permissions';
import { JobsTranslationKeys } from './jobs-translation-keys';
import { catalog, jobRun } from './jobs.testing';

const Keys = JobsTranslationKeys;
type FakeClient = Record<'list' | 'run' | 'runs', ReturnType<typeof vi.fn>>;

function dialogOf(host: HTMLElement): HTMLElement {
  return host.querySelector('dialog[open]') as HTMLElement;
}

async function submit(fixture: ComponentFixture<unknown>, host: HTMLElement): Promise<void> {
  (dialogOf(host).querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit', { cancelable: true }));
  await settle(fixture);
}

describe('jobs screens', () => {
  let client: FakeClient;

  beforeEach(() => {
    client = {
      list: vi.fn().mockReturnValue(of(catalog())),
      run: vi.fn().mockReturnValue(of({ runId: 'run-2' })),
      runs: vi.fn().mockReturnValue(of([
        jobRun({ id: 'run-2' as never, status: JobRunStatus.Failed, trigger: JobTrigger.Manual, triggeredBy: 'admin', referenceCode: 'REF-42' }),
        jobRun({ startedAt: null, finishedAt: null, status: JobRunStatus.Queued }),
      ])),
    };
    TestBed.configureTestingModule({
      imports: [JobsPageComponent, JobRunsPageComponent],
      providers: [...adminTestProviders(), { provide: AdminJobsClient, useValue: client }],
    });
  });

  describe('JobsPageComponent', () => {
    let fixture: ComponentFixture<JobsPageComponent>;
    let host: HTMLElement;

    async function render(): Promise<void> {
      fixture = TestBed.createComponent(JobsPageComponent);
      host = fixture.nativeElement as HTMLElement;
      fixture.detectChanges();
      await settle(fixture);
    }

    it('lists the jobs with schedule, last run status and history links', async () => {
      grant(JobsPermissionKeys.View);
      await render();
      const rows = host.querySelectorAll('tbody tr');
      expect(rows).toHaveLength(2);
      expect(rows[0].textContent).toContain('تفريغ صندوق الصادر');
      expect(allByTestId(fixture, 'job-cron').map((cell) => cell.textContent?.trim())).toEqual(['*/5 * * * *', '0 3 * * *']);
      const status = allByTestId(fixture, 'job-last-status');
      expect(status[0].textContent).toContain(Keys.Status.Succeeded);
      expect(status[1].textContent).toContain(Keys.LastRun.Never);
      expect(allByTestId(fixture, 'job-history')[0].getAttribute('href')).toBe('/admin/jobs/outbox-drain/runs');
    });

    it('hides Run now from a viewer', async () => {
      grant(JobsPermissionKeys.View);
      await render();
      expect(allByTestId(fixture, 'job-run')).toHaveLength(0);
      expect(host.querySelector('app-editor-dialog-pattern')).toBeNull();
    });

    it('asks before running, then opens the run history', async () => {
      grant(JobsPermissionKeys.View, JobsPermissionKeys.Run);
      await render();
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      press(allByTestId(fixture, 'job-run')[1]);
      await settle(fixture);
      expect(dialogOf(host).textContent).toContain(`${Keys.Run.Text}|name=تنظيف سجل الإعادة`);
      expect(client.run).not.toHaveBeenCalled();
      await submit(fixture, host);
      expect(client.run).toHaveBeenCalledWith('security-replay-purge');
      expect(navigate).toHaveBeenCalledWith(['/', 'admin', 'jobs', 'security-replay-purge', 'runs']);
    });

    it('shows a refused run in the dialog and stays', async () => {
      client.run.mockReturnValue(throwError(() => problem(HttpStatus.forbidden, 'FORBIDDEN')));
      grant(JobsPermissionKeys.View, JobsPermissionKeys.Run);
      await render();
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate');
      press(allByTestId(fixture, 'job-run')[0]);
      await settle(fixture);
      await submit(fixture, host);
      expect(dialogOf(host).textContent).toContain('errors.FORBIDDEN');
      expect(navigate).not.toHaveBeenCalled();
    });

    it('shows the error state when the catalog fails', async () => {
      client.list.mockReturnValue(throwError(() => problem(HttpStatus.serverError, 'INTERNAL_ERROR')));
      grant(JobsPermissionKeys.View);
      await render();
      expect(host.querySelector('[role="alert"]')?.textContent).toContain('errors.INTERNAL_ERROR');
    });
  });

  describe('JobRunsPageComponent', () => {
    let fixture: ComponentFixture<JobRunsPageComponent>;
    let host: HTMLElement;

    async function render(jobKey = 'outbox-drain'): Promise<void> {
      fixture = TestBed.createComponent(JobRunsPageComponent);
      host = fixture.nativeElement as HTMLElement;
      fixture.componentRef.setInput('jobKey', jobKey);
      fixture.detectChanges();
      await settle(fixture);
    }

    it('shows the run history: status, trigger, system actor, triggered by, times and reference code', async () => {
      grant(JobsPermissionKeys.View);
      await render();
      expect(client.runs).toHaveBeenCalledWith('outbox-drain');
      expect(byTestId(fixture, 'runs-job')?.textContent).toContain(`${Keys.Runs.Job}|name=تفريغ صندوق الصادر`);
      const rows = host.querySelectorAll('tbody tr');
      expect(rows).toHaveLength(2);
      expect(rows[0].textContent).toContain(Keys.Status.Failed);
      expect(rows[0].textContent).toContain(Keys.Triggers.Manual);
      expect(rows[0].textContent).toContain(Keys.Runs.ActorSystem);
      expect(rows[0].textContent).toContain('admin');
      expect(byTestId(fixture, 'run-reference')?.textContent).toBe('REF-42');
      expect(rows[1].textContent).toContain(Keys.Triggers.Schedule);
      expect(rows[1].textContent).toContain(Keys.Runs.NoUser);
      expect(rows[1].textContent).toContain(Keys.Runs.NotYet);
      expect(byTestId(fixture, 'runs-back')?.getAttribute('href')).toBe('/admin/jobs');
    });

    it('refreshes on demand; Run now only for jobs.run', async () => {
      grant(JobsPermissionKeys.View);
      await render();
      expect(byTestId(fixture, 'runs-run')).toBeNull();
      press(byTestId(fixture, 'runs-refresh'));
      await settle(fixture);
      expect(client.runs.mock.calls.length).toBeGreaterThanOrEqual(2);
    });

    it('runs the job from the history and reloads it', async () => {
      grant(JobsPermissionKeys.View, JobsPermissionKeys.Run);
      await render();
      const before = client.runs.mock.calls.length;
      press(byTestId(fixture, 'runs-run'));
      await settle(fixture);
      expect(dialogOf(host).textContent).toContain(Keys.Run.Title);
      await submit(fixture, host);
      expect(client.run).toHaveBeenCalledWith('outbox-drain');
      expect(client.runs.mock.calls.length).toBeGreaterThan(before);
      expect(host.querySelector('dialog[open]')).toBeNull();
    });

    it('shows the job key and the refusal for an unknown job', async () => {
      client.runs.mockReturnValue(throwError(() => problem(HttpStatus.notFound, 'NOT_FOUND')));
      grant(JobsPermissionKeys.View);
      await render('gone');
      expect(byTestId(fixture, 'runs-job')?.textContent).toContain(`${Keys.Runs.Job}|name=gone`);
      expect(host.querySelector('[role="alert"]')?.textContent).toContain('errors.NOT_FOUND');
    });
  });
});
