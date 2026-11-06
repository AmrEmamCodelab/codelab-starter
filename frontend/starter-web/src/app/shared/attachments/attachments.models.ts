import type { WireId } from '@codelab/ng-security';

/*
 * Hand-written wire shapes of the attachment endpoints (Phase 6 contract, sections 1 and 3). They mirror the API and
 * the attachment service exactly, so this adapter does not wait on the generated client; the generated types under
 * `core/api/generated` may replace them later without changing the adapter's signatures.
 */

/** An attachment link's PublicId: opaque, sent back exactly as received. */
export type AttachmentId = WireId<'attachment'>;

/**
 * The PublicId of the record attachments hang on. Any record type's id fits (its domain varies with the record type),
 * so a feature passes its own `WireId<'service-request'>` unchanged.
 */
export type AttachableRecordId = WireId<string>;

/** One attachment type of a record type (`GET api/attachments/types?recordType=`). Extensions are lowercase with the dot. */
export interface AttachmentType {
  readonly key: string;
  readonly nameAr: string;
  readonly nameEn: string;
  readonly isRequired: boolean;
  readonly maxFiles: number;
  readonly allowedExtensions: readonly string[];
}

/** One linked attachment. `uploadedAt` is ISO 8601 UTC; `fileName` is the sanitized client name. */
export interface RecordAttachment {
  readonly id: AttachmentId;
  readonly typeKey: string;
  readonly fileName: string;
  readonly sizeBytes: number;
  readonly contentType: string;
  readonly uploadedAt: string;
}

/** `GET api/records/{recordType}/{recordId}/attachments`: the links, and the required types that have none yet. */
export interface RecordAttachmentList {
  readonly items: readonly RecordAttachment[];
  readonly missingRequiredTypeKeys: readonly string[];
}

/** The body of `POST .../attachments/upload-ticket`. */
export interface UploadTicketRequest {
  readonly typeKey: string;
}

/**
 * A short-lived, single-use upload ticket. `uploadUrl` is the attachment service's absolute upload URL; the ticket goes
 * in the header named `ticketHeader` (`X-{Prefix}-Upload-Ticket`) and nowhere else. `expiresAt` is ISO 8601 UTC.
 */
export interface UploadTicket {
  readonly ticket: string;
  readonly ticketHeader: string;
  readonly uploadUrl: string;
  readonly expiresAt: string;
  readonly allowedExtensions: readonly string[];
  readonly maxFileCount: number;
}

/** One file the attachment service stored. `receipt` is the signed proof the API links with; `fileId` stays opaque. */
export interface StoredFile {
  readonly fileId: string;
  readonly fileName: string;
  readonly sizeBytes: number;
  readonly contentType: string;
  readonly sha256: string;
  readonly receipt: string;
}

/** The attachment service's answer to `POST /api/uploads`. */
export interface UploadResponse {
  readonly files: readonly StoredFile[];
}

/** The body of `POST api/records/{recordType}/{recordId}/attachments`. */
export interface LinkAttachmentRequest {
  readonly typeKey: string;
  readonly receipt: string;
}

/** `GET api/attachments/demo-record` (Development only): the record the demo page attaches to. */
export interface AttachmentDemoRecord {
  readonly recordType: string;
  readonly recordId: AttachableRecordId;
}

/** What an upload stream emits. */
export enum AttachmentUploadEventKind {
  /** Bytes are on their way to the attachment service. */
  Progress = 0,
  /** The service stored the file and answered its receipt (the upload service's last event). */
  Stored = 1,
  /** The API linked the stored file to the record (the adapter's last event). */
  Linked = 2,
}

/** Upload progress. `totalBytes` is null when the browser does not know it; `percent` is then 0. */
export interface AttachmentUploadProgress {
  readonly kind: AttachmentUploadEventKind.Progress;
  readonly loadedBytes: number;
  readonly totalBytes: number | null;
  /** Whole percent, 0..100. */
  readonly percent: number;
}

/** The attachment service stored the file. */
export interface AttachmentStored {
  readonly kind: AttachmentUploadEventKind.Stored;
  readonly file: StoredFile;
}

/** The API linked the file to the record. */
export interface AttachmentLinked {
  readonly kind: AttachmentUploadEventKind.Linked;
  readonly attachment: RecordAttachment;
}

/** What `AttachmentUploadService.upload` emits: progress, then exactly one `Stored`. */
export type FileUploadEvent = AttachmentUploadProgress | AttachmentStored;

/** What `RecordAttachmentsAdapter.upload` emits: progress, then exactly one `Linked`. */
export type AttachmentUploadEvent = AttachmentUploadProgress | AttachmentLinked;
