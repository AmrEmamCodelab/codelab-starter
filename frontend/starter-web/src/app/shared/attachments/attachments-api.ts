import { API_PREFIX } from '../../core/api/clients/api-paths';

/** Path segments of the attachment endpoints (the backend Attachments module's routes). */
const Segments = {
  Attachments: 'attachments',
  Records: 'records',
  Types: 'types',
  UploadTicket: 'upload-ticket',
  Content: 'content',
  DemoRecord: 'demo-record',
} as const;

const SEPARATOR = '/';

function joinPath(...segments: readonly string[]): string {
  return segments.join(SEPARATOR);
}

/** A caller-supplied value as exactly one path segment: it can never add a segment or a query. */
function segment(value: string): string {
  return encodeURIComponent(value);
}

function recordAttachments(recordType: string, recordId: string): string {
  return joinPath(API_PREFIX, Segments.Records, segment(recordType), segment(recordId), Segments.Attachments);
}

function attachment(id: string): string {
  return joinPath(API_PREFIX, Segments.Attachments, segment(id));
}

/**
 * The attachment endpoints of the API, relative to the API root (no leading slash). Record types and ids are
 * URL-encoded into their own segment.
 */
export const AttachmentsApiPaths = {
  /** GET `?recordType=`: the attachment types of a record type. */
  Types: joinPath(API_PREFIX, Segments.Attachments, Segments.Types),
  /** GET, Development only: the demo record. */
  DemoRecord: joinPath(API_PREFIX, Segments.Attachments, Segments.DemoRecord),
  /** GET the list, POST a link. */
  recordAttachments,
  /** POST: an upload ticket for one attachment type. */
  uploadTicket: (recordType: string, recordId: string): string =>
    joinPath(recordAttachments(recordType, recordId), Segments.UploadTicket),
  /** DELETE: soft-removes one attachment. */
  attachment,
  /** GET: the attachment's bytes, streamed by the API from the attachment service. */
  content: (id: string): string => joinPath(attachment(id), Segments.Content),
} as const;

/** Query parameter names the attachment endpoints read. */
export const AttachmentsQueryParams = {
  RecordType: 'recordType',
} as const;

/** The multipart part name the attachment service reads files from. */
export const UPLOAD_FILE_PART = 'file';

/**
 * The only header name an upload ticket may travel in: `X-{Prefix}-Upload-Ticket`. A ticket response naming any other
 * header (such as `Authorization`) is refused, so a misconfigured API can never make the browser send the ticket as
 * something else.
 */
export const UPLOAD_TICKET_HEADER_PATTERN = /^X-[A-Za-z][A-Za-z0-9]*-Upload-Ticket$/;
