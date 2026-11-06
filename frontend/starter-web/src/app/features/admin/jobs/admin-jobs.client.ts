import { HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiService } from '../../../core/api/api.service';
import { API_PREFIX } from '../../../core/api/clients/api-paths';
import { quietContext } from '../../../core/http/http-context';
import type { JobRun, JobRunAccepted, JobSummary } from './job.models';

const JOBS_ROOT = `${API_PREFIX}/admin/jobs`;

function runsPath(jobKey: string): string {
  return `${JOBS_ROOT}/${encodeURIComponent(jobKey)}/runs`;
}

/** The background jobs endpoints, relative to the API root (no leading slash). */
export const JobsApiPaths = {
  Jobs: JOBS_ROOT,
  runs: runsPath,
} as const;

/** Query parameter names of `GET api/admin/jobs/{key}/runs`. */
export const JobsApiQueryParams = {
  Take: 'take',
} as const;

/** How many of a job's latest runs the history asks for (the server's default page). */
export const JOB_RUNS_TAKE = 20;

/**
 * The background jobs client (`jobs.view` / `jobs.run`), a thin wrapper over `ApiService`. Every call is quiet (no
 * error dialog): the screens show refusals inline with the reference code. An unknown job key is a 404 problem.
 */
@Injectable({ providedIn: 'root' })
export class AdminJobsClient {
  private readonly api = inject(ApiService);

  /** The job catalog, each job with its last run. */
  list(): Observable<readonly JobSummary[]> {
    return this.api.get<readonly JobSummary[]>(JobsApiPaths.Jobs, { context: quietContext() });
  }

  /** Queues a run of the job now (as the system actor); answers its id. */
  run(jobKey: string): Observable<JobRunAccepted> {
    return this.api.post<JobRunAccepted>(JobsApiPaths.runs(jobKey), null, { context: quietContext() });
  }

  /** The job's latest runs, newest first. */
  runs(jobKey: string, take: number = JOB_RUNS_TAKE): Observable<readonly JobRun[]> {
    return this.api.get<readonly JobRun[]>(JobsApiPaths.runs(jobKey), {
      params: new HttpParams().set(JobsApiQueryParams.Take, take),
      context: quietContext(),
    });
  }
}
