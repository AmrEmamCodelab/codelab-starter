import { Injectable, inject } from '@angular/core';
import { of, type Observable } from 'rxjs';
import { catchError, concatMap, map } from 'rxjs/operators';
import { rethrowAsAttachmentError } from './attachment-error';
import { AttachmentUploadService } from './attachment-upload.service';
import { AttachmentsClient } from './attachments.client';
import {
  AttachmentUploadEventKind,
  type AttachableRecordId,
  type AttachmentId,
  type AttachmentType,
  type AttachmentUploadEvent,
  type FileUploadEvent,
  type RecordAttachment,
  type RecordAttachmentList,
} from './attachments.models';
import { FileSaver } from './file-saver';

/**
 * The one entry point screens use for a record's attachments (`app-record-attachments`, the demo page, record
 * screens). It hides the upload chain: ticket from the API → multipart upload to the attachment service → receipt →
 * link on the API. Every failure, from either server or from the browser, is an `AttachmentError`
 * (`code`, `referenceCode`, `status`); render `errors.<code>`.
 *
 * Positional `(recordType, recordId, typeKey)` follows the Phase 6 contract; `recordId` is a branded `WireId`, so it
 * cannot be swapped with either string.
 */
@Injectable({ providedIn: 'root' })
export class RecordAttachmentsAdapter {
  private readonly client = inject(AttachmentsClient);
  private readonly uploads = inject(AttachmentUploadService);
  private readonly files = inject(FileSaver);

  /** The attachment types of `recordType` (labels, required flag, limits, allowed extensions). */
  types(recordType: string): Observable<readonly AttachmentType[]> {
    return this.client.types(recordType).pipe(catchError(rethrowAsAttachmentError));
  }

  /** The record's attachments and the required types still missing. */
  list(recordType: string, recordId: AttachableRecordId): Observable<RecordAttachmentList> {
    return this.client.list(recordType, recordId).pipe(catchError(rethrowAsAttachmentError));
  }

  /**
   * Uploads `file` as an attachment of type `typeKey` and links it to the record. Emits upload progress, then exactly
   * one `Linked` event carrying the new attachment, then completes. A failed step stops the chain: a refused ticket
   * sends no bytes, and a refused upload links nothing. Unsubscribing cancels the running step.
   */
  upload(recordType: string, recordId: AttachableRecordId, typeKey: string, file: File): Observable<AttachmentUploadEvent> {
    return this.client.uploadTicket(recordType, recordId, { typeKey }).pipe(
      concatMap((ticket) => this.uploads.upload(ticket, file)),
      concatMap((event) => this.linkWhenStored(recordType, recordId, typeKey, event)),
      catchError(rethrowAsAttachmentError),
    );
  }

  /** Fetches the attachment's bytes and saves them under its file name. Emits once when the save started. */
  download(attachment: RecordAttachment): Observable<void> {
    return this.client.content(attachment.id).pipe(
      map((blob) => this.files.save(blob, attachment.fileName)),
      catchError(rethrowAsAttachmentError),
    );
  }

  /** Removes the attachment from its record (the stored file is cleaned up by the API later). */
  remove(id: AttachmentId): Observable<void> {
    return this.client.remove(id).pipe(catchError(rethrowAsAttachmentError));
  }

  private linkWhenStored(
    recordType: string,
    recordId: AttachableRecordId,
    typeKey: string,
    event: FileUploadEvent,
  ): Observable<AttachmentUploadEvent> {
    if (event.kind === AttachmentUploadEventKind.Progress) return of(event);
    return this.client
      .link(recordType, recordId, { typeKey, receipt: event.file.receipt })
      .pipe(map((attachment) => ({ kind: AttachmentUploadEventKind.Linked, attachment })));
  }
}
