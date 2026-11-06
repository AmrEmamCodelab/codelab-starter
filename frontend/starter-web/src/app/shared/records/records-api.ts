import { API_PREFIX } from '../../core/api/clients/api-paths';

/** Path segments of the generic record and queue endpoints (the backend Records and Assignments modules' routes). */
const Segments = {
  Records: 'records',
  Queues: 'queues',
  Mine: 'mine',
  Oversight: 'oversight',
  Timeline: 'timeline',
  Transitions: 'transitions',
  Claim: 'claim',
  Release: 'release',
  Assign: 'assign',
} as const;

const SEPARATOR = '/';

function joinPath(...segments: readonly string[]): string {
  return segments.join(SEPARATOR);
}

/** A caller-supplied value as exactly one path segment: it can never add a segment or a query. */
function segment(value: string): string {
  return encodeURIComponent(value);
}

function recordsOf(recordType: string): string {
  return joinPath(API_PREFIX, Segments.Records, segment(recordType));
}

function record(recordType: string, id: string): string {
  return joinPath(recordsOf(recordType), segment(id));
}

function queueOf(recordType: string): string {
  return joinPath(API_PREFIX, Segments.Queues, segment(recordType));
}

function queueItem(recordType: string, id: string): string {
  return joinPath(queueOf(recordType), segment(id));
}

/**
 * The generic record endpoints (Records module), relative to the API root (no leading slash). The record type and
 * the PublicId are URL-encoded into their own segment.
 */
export const RecordsApiPaths = {
  /** GET: the caller's own records of a type (paged). */
  mine: (recordType: string): string => joinPath(recordsOf(recordType), Segments.Mine),
  /** GET: the admin read-only list of every record of a type (paged). */
  oversight: (recordType: string): string => joinPath(recordsOf(recordType), Segments.Oversight),
  /** GET: one record with its server-computed allowed actions. */
  record,
  /** GET: the record's transition history. */
  timeline: (recordType: string, id: string): string => joinPath(record(recordType, id), Segments.Timeline),
  /** POST: apply one workflow action. */
  transitions: (recordType: string, id: string): string => joinPath(record(recordType, id), Segments.Transitions),
} as const;

/** The work queue endpoints (Assignments module), relative to the API root. */
export const QueuesApiPaths = {
  /** GET: the queue of a record type (paged, by view). */
  queue: queueOf,
  /** POST: the caller takes the record. */
  claim: (recordType: string, id: string): string => joinPath(queueItem(recordType, id), Segments.Claim),
  /** POST: the caller gives the record back to the queue. */
  release: (recordType: string, id: string): string => joinPath(queueItem(recordType, id), Segments.Release),
  /** POST `{ userId }`: hand the record to another eligible user. */
  assign: (recordType: string, id: string): string => joinPath(queueItem(recordType, id), Segments.Assign),
} as const;

/** Query parameter names the record and queue list endpoints read. */
export const RecordsQueryParams = {
  Status: 'status',
  Number: 'number',
  Page: 'page',
  PageSize: 'pageSize',
  View: 'view',
  SubmittedFrom: 'submittedFrom',
  SubmittedTo: 'submittedTo',
} as const;
