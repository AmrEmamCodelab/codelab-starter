/**
 * The JSON the Ops Console API's BFF (`/ops-api/*`, port 5188 locally) answers with. Every enum travels as its integer
 * value; timestamps are ISO 8601 strings in UTC.
 */

/** GET /ops-api/me: the signed-in operator (Windows / AD identity). */
export interface OperatorProfile {
  readonly name: string;
  readonly groups: readonly string[];
}

/** The health of one check or of the whole system. */
export enum OpsHealthStatus {
  Healthy = 1,
  Degraded = 2,
  Unhealthy = 3,
}

/** One health check: a dependency (database, gateway, Seq) or a deployable. */
export interface OpsHealthCheck {
  readonly name: string;
  readonly status: OpsHealthStatus;
  /** How long the check took, in milliseconds. */
  readonly durationMs: number;
  /** A short operator-facing explanation, or null when healthy. */
  readonly description: string | null;
}

/** GET /ops-api/health: the overall status (the worst check) and every check. */
export interface OpsHealthReport {
  readonly status: OpsHealthStatus;
  readonly checkedAt: string;
  readonly checks: readonly OpsHealthCheck[];
}

/** The state of a job run; mirrors the API's JobRunStatus (same integers). */
export enum JobRunStatus {
  Queued = 1,
  Running = 2,
  Succeeded = 3,
  Failed = 4,
}

/** GET /ops-api/jobs: one recurring background job and its last run. */
export interface OpsJob {
  /** The job key (stable identifier, shown as is). */
  readonly key: string;
  /** The last run's state, or null when the job never ran. */
  readonly lastStatus: JobRunStatus | null;
  readonly lastStartedAt: string | null;
  readonly lastFinishedAt: string | null;
  /** The next scheduled run, or null when the job is not scheduled. */
  readonly nextRunAt: string | null;
}

/** Certificate expiry state, from the API's `Codelab:Ops` warning and critical day thresholds. */
export enum CertificateStatus {
  Ok = 1,
  Warning = 2,
  Critical = 3,
  Expired = 4,
}

/** GET /ops-api/certificates: one certificate the deployment depends on. */
export interface OpsCertificate {
  readonly name: string;
  readonly subject: string;
  readonly notAfter: string;
  /** Whole days until `notAfter`; negative once expired. */
  readonly daysLeft: number;
  readonly status: CertificateStatus;
}

/** The request a reference code belongs to. */
export interface ReferenceRequest {
  readonly method: string;
  readonly path: string;
  readonly status: number;
}

/** One log event of the traced request, as stored in Seq. `level` is Seq's level name (`Information`, `Error`...). */
export interface ReferenceEvent {
  readonly timestamp: string;
  readonly level: string;
  readonly message: string;
}

/**
 * GET /ops-api/reference-codes/{code}: what the API and the logs hold for a reference code (404 when nothing matches).
 * A part the BFF could not find (Seq down, request not logged) is null; `seqAvailable` is false when Seq did not answer.
 */
export interface ReferenceTrace {
  readonly referenceCode: string;
  readonly correlationId: string | null;
  readonly traceId: string | null;
  readonly firstSeen: string | null;
  readonly request: ReferenceRequest | null;
  readonly events: readonly ReferenceEvent[];
  /** A deep link to the matching events in Seq, or null. */
  readonly seqUrl: string | null;
  readonly seqAvailable: boolean;
}
