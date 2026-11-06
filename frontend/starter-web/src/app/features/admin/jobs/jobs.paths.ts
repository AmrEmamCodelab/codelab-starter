import { RoutePaths } from '../../../core/routing/route-paths';

/** The root segment every absolute in-app link starts from. */
const ROOT_SEGMENT = '/';

/**
 * The segment the admin routes mount `JOBS_ROUTES` on (`/admin/jobs`). `AdminRouteSegments` in `admin.paths.ts` uses
 * this same value, so the two never drift.
 */
export const JOBS_MOUNT_SEGMENT = 'jobs';

/** Route parameter names of the jobs screens (bound to component inputs of the same name). */
export const JobsRouteParams = {
  JobKey: 'jobKey',
} as const;

const RUNS_SEGMENT = 'runs';

/** The paths of `JOBS_ROUTES`, relative to the mount. */
export const JobsRouteSegments = {
  List: '',
  Runs: `:${JobsRouteParams.JobKey}/${RUNS_SEGMENT}`,
} as const;

function jobsLink(...segments: readonly string[]): readonly string[] {
  return [ROOT_SEGMENT, RoutePaths.Admin, JOBS_MOUNT_SEGMENT, ...segments];
}

/** Absolute router commands for the jobs screens: `[routerLink]="JobsPaths.runs(job.key)"`. */
export const JobsPaths = {
  list: (): readonly string[] => jobsLink(),
  runs: (jobKey: string): readonly string[] => jobsLink(jobKey, RUNS_SEGMENT),
} as const;
