import { HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiService } from '../../core/api/api.service';
import { quietContext } from '../../core/http/http-context';
import { AttachmentsApiPaths, AttachmentsQueryParams } from './attachments-api';
import type {
  AttachableRecordId,
  AttachmentDemoRecord,
  AttachmentId,
  AttachmentType,
  LinkAttachmentRequest,
  RecordAttachment,
  RecordAttachmentList,
  UploadTicket,
  UploadTicketRequest,
} from './attachments.models';

/**
 * The API's attachment endpoints, a thin wrapper over `ApiService` (bearer token, request signing). Every call is
 * quiet: the attachment UI shows its own failures (no app-wide error dialog), while a 401 still ends an expired
 * session. Failures arrive as the raw `HttpErrorResponse`; {@link RecordAttachmentsAdapter} turns them into
 * `AttachmentError`. Permissions: `attachments.view` to read, `attachments.upload` for tickets and links,
 * `attachments.remove` to delete.
 */
@Injectable({ providedIn: 'root' })
export class AttachmentsClient {
  private readonly api = inject(ApiService);

  /** The attachment types of `recordType`. 404 `ATTACHMENTS_RECORD_TYPE_UNKNOWN`. */
  types(recordType: string): Observable<readonly AttachmentType[]> {
    return this.api.get<readonly AttachmentType[]>(AttachmentsApiPaths.Types, {
      params: new HttpParams().set(AttachmentsQueryParams.RecordType, recordType),
      context: quietContext(),
    });
  }

  /** The record's attachments and its missing required types. */
  list(recordType: string, recordId: AttachableRecordId): Observable<RecordAttachmentList> {
    return this.api.get<RecordAttachmentList>(AttachmentsApiPaths.recordAttachments(recordType, recordId), {
      context: quietContext(),
    });
  }

  /** A single-use upload ticket for one attachment type of the record. */
  uploadTicket(recordType: string, recordId: AttachableRecordId, body: UploadTicketRequest): Observable<UploadTicket> {
    return this.api.post<UploadTicket>(AttachmentsApiPaths.uploadTicket(recordType, recordId), body, {
      context: quietContext(),
    });
  }

  /** Links a stored file (by its receipt) to the record; answers the new attachment (201). */
  link(recordType: string, recordId: AttachableRecordId, body: LinkAttachmentRequest): Observable<RecordAttachment> {
    return this.api.post<RecordAttachment>(AttachmentsApiPaths.recordAttachments(recordType, recordId), body, {
      context: quietContext(),
    });
  }

  /** The attachment's bytes. */
  content(id: AttachmentId): Observable<Blob> {
    return this.api.getBlob(AttachmentsApiPaths.content(id), { context: quietContext() });
  }

  /** Soft-removes the attachment (204); the stored file is deleted later by the API's outbox. */
  remove(id: AttachmentId): Observable<void> {
    return this.api.delete<void>(AttachmentsApiPaths.attachment(id), { context: quietContext() });
  }

  /** The Development demo record (404 outside Development). */
  demoRecord(): Observable<AttachmentDemoRecord> {
    return this.api.get<AttachmentDemoRecord>(AttachmentsApiPaths.DemoRecord, { context: quietContext() });
  }
}
