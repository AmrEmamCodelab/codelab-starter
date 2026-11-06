import { describe, expect, it } from 'vitest';
import { RouteDataKeys } from '../../../core/routing/route-paths';
import { JobsPermissionKeys } from './jobs-permissions';
import { JOBS_ROUTES } from './jobs.routes';
import { JOBS_MOUNT_SEGMENT, JobsPaths, JobsRouteParams, JobsRouteSegments } from './jobs.paths';

describe('JOBS_ROUTES', () => {
  it('gates both pages with jobs.view', () => {
    expect(JOBS_ROUTES).toHaveLength(2);
    for (const route of JOBS_ROUTES) {
      expect(route.canActivate).toHaveLength(1);
      expect(route.data?.[RouteDataKeys.Permission]).toBe(JobsPermissionKeys.View);
    }
    expect(JobsPermissionKeys).toEqual({ View: 'jobs.view', Run: 'jobs.run' });
  });

  it('lazy-loads the catalog and the run history', async () => {
    const [list, runs] = await Promise.all(JOBS_ROUTES.map((route) => route.loadComponent?.()));
    expect((list as { name: string }).name).toBe('JobsPageComponent');
    expect((runs as { name: string }).name).toBe('JobRunsPageComponent');
  });

  it('binds the job key from the runs path', () => {
    expect(JobsRouteSegments.Runs).toBe(`:${JobsRouteParams.JobKey}/runs`);
    expect(JobsPaths.list()).toEqual(['/', 'admin', JOBS_MOUNT_SEGMENT]);
    expect(JobsPaths.runs('outbox-drain')).toEqual(['/', 'admin', JOBS_MOUNT_SEGMENT, 'outbox-drain', 'runs']);
  });
});
