import { HttpEventType, type HttpEvent } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { EMPTY, of, throwError, type Observable } from 'rxjs';
import { catchError, concatMap } from 'rxjs/operators';
import { ApiService } from '../../core/api/api.service';
import { clientAttachmentError, rethrowAsAttachmentError } from './attachment-error';
import { UPLOAD_FILE_PART, UPLOAD_TICKET_HEADER_PATTERN } from './attachments-api';
import {
  AttachmentUploadEventKind,
  type AttachmentUploadProgress,
  type FileUploadEvent,
  type UploadResponse,
  type UploadTicket,
} from './attachments.models';

/** Whole percent of a finished upload. */
const FULL_PERCENT = 100;

/**
 * Sends one file to the attachment service with an upload ticket: a multipart POST to the ticket's `uploadUrl` that
 * carries the ticket header and nothing else (no bearer token, no signature, no cookies; see
 * `ApiService.uploadMultipart`, which also refuses a URL outside `origins.attachment`).
 *
 * Emits progress, then exactly one `Stored` with the service's receipt. Every failure is an `AttachmentError`
 * (the service's refusal codes, such as `ATTACHMENTS_FILE_TOO_LARGE` with its `referenceCode`).
 */
@Injectable({ providedIn: 'root' })
export class AttachmentUploadService {
  private readonly api = inject(ApiService);

  upload(ticket: UploadTicket, file: File): Observable<FileUploadEvent> {
    if (!UPLOAD_TICKET_HEADER_PATTERN.test(ticket.ticketHeader)) {
      return throwError(() => clientAttachmentError());
    }
    const form = new FormData();
    form.append(UPLOAD_FILE_PART, file, file.name);
    return this.api.uploadMultipart<UploadResponse>(ticket.uploadUrl, form, { [ticket.ticketHeader]: ticket.ticket }).pipe(
      concatMap((event) => toUploadEvent(event)),
      catchError(rethrowAsAttachmentError),
    );
  }
}

function toUploadEvent(event: HttpEvent<UploadResponse>): Observable<FileUploadEvent> {
  if (event.type === HttpEventType.UploadProgress) return of(progressOf(event.loaded, event.total));
  if (event.type !== HttpEventType.Response) return EMPTY;
  const stored = event.body?.files[0];
  if (stored === undefined) return throwError(() => clientAttachmentError(event.status));
  return of({ kind: AttachmentUploadEventKind.Stored, file: stored });
}

/** Progress in bytes and whole percent; an unknown total reports 0 percent. */
export function progressOf(loadedBytes: number, totalBytes: number | undefined): AttachmentUploadProgress {
  const total = totalBytes !== undefined && totalBytes > 0 ? totalBytes : null;
  const percent = total === null ? 0 : Math.min(FULL_PERCENT, Math.floor((loadedBytes * FULL_PERCENT) / total));
  return { kind: AttachmentUploadEventKind.Progress, loadedBytes, totalBytes: total, percent };
}
