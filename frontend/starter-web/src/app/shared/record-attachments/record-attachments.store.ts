import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { EMPTY, Subject, forkJoin, from, type Observable } from 'rxjs';
import { catchError, concatMap, takeUntil, tap } from 'rxjs/operators';
import { TranslationService } from '../../core/i18n/translation.service';
import { AttachmentError, clientAttachmentError } from '../attachments/attachment-error';
import {
  AttachmentUploadEventKind,
  type AttachableRecordId,
  type AttachmentType,
  type AttachmentUploadEvent,
  type RecordAttachment,
} from '../attachments/attachments.models';
import { RecordAttachmentsAdapter } from '../attachments/record-attachments.adapter';
import { RecordAttachmentsTranslationKeys as Keys } from './record-attachments-keys';
import {
  AttachmentAction,
  RecordAttachmentsLoadState,
  failedUpload,
  failureFeedback,
  isUploadRowId,
  queuedUpload,
  successFeedback,
  uploadRowId,
  uploadingUpload,
  type AttachmentsFeedback,
  type UploadRow,
} from './record-attachments-view';

/** The record the widget shows. */
interface RecordRef {
  readonly recordType: string;
  readonly recordId: AttachableRecordId;
}

function asAttachmentError(failure: unknown): AttachmentError {
  return failure instanceof AttachmentError ? failure : clientAttachmentError();
}

/**
 * The state behind one `app-record-attachments` (provided by the component, one per widget). It loads the record's
 * types and attachments, uploads picked files one at a time through {@link RecordAttachmentsAdapter}, downloads and
 * removes, and reports every outcome as page feedback. The server stays authoritative: the required-missing list is
 * the server's, refreshed after every change.
 *
 * Switching to another record cancels the uploads still running for the previous one.
 */
@Injectable()
export class RecordAttachmentsStore {
  private readonly adapter = inject(RecordAttachmentsAdapter);
  private readonly translations = inject(TranslationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly recordChanged = new Subject<void>();
  private record: RecordRef | null = null;
  private uploadSequence = 0;

  readonly loadState = signal(RecordAttachmentsLoadState.Loading);
  readonly types = signal<readonly AttachmentType[]>([]);
  readonly attachments = signal<readonly RecordAttachment[]>([]);
  readonly missingTypeKeys = signal<readonly string[]>([]);
  readonly uploads = signal<readonly UploadRow[]>([]);
  /** The attachment being downloaded (one at a time), or null. */
  readonly downloadingId = signal<string | null>(null);
  /** The attachment being removed (one at a time), or null. */
  readonly removingId = signal<string | null>(null);
  readonly feedback = signal<AttachmentsFeedback | null>(null);

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.recordChanged.next();
      this.recordChanged.complete();
    });
  }

  /** Shows `recordType`/`recordId`: loads its types and attachments, dropping the previous record's uploads. */
  load(recordType: string, recordId: AttachableRecordId): void {
    this.recordChanged.next();
    this.record = { recordType, recordId };
    this.uploads.set([]);
    this.feedback.set(null);
    this.loadState.set(RecordAttachmentsLoadState.Loading);
    forkJoin({ types: this.adapter.types(recordType), list: this.adapter.list(recordType, recordId) })
      .pipe(takeUntil(this.recordChanged))
      .subscribe({
        next: ({ types, list }) => {
          this.types.set(types);
          this.attachments.set(list.items);
          this.missingTypeKeys.set(list.missingRequiredTypeKeys);
          this.loadState.set(RecordAttachmentsLoadState.Ready);
        },
        error: (failure: unknown) => {
          this.loadState.set(RecordAttachmentsLoadState.Failed);
          this.fail(AttachmentAction.Load, failure);
        },
      });
  }

  /** Loads the current record again (the retry after a failed load). */
  retry(): void {
    if (this.record) this.load(this.record.recordType, this.record.recordId);
  }

  /** Queues `files` as attachments of type `typeKey` and uploads them one after another. */
  upload(typeKey: string, files: readonly File[]): void {
    const record = this.record;
    if (!record || files.length === 0) return;
    const picked = files.map((file) => ({ file, row: queuedUpload(this.nextUploadId(), typeKey, file) }));
    this.uploads.update((rows) => [...rows, ...picked.map(({ row }) => row)]);
    from(picked)
      .pipe(
        concatMap(({ file, row }) => this.uploadOne(record, row, file)),
        takeUntil(this.recordChanged),
      )
      .subscribe();
  }

  /** Saves a linked attachment's file. Ignored while another download runs. */
  download(attachmentId: string): void {
    const attachment = this.findAttachment(attachmentId);
    if (!attachment || this.downloadingId() !== null) return;
    this.downloadingId.set(attachment.id);
    this.adapter
      .download(attachment)
      .pipe(takeUntil(this.recordChanged))
      .subscribe({
        complete: () => this.downloadingId.set(null),
        error: (failure: unknown) => {
          this.downloadingId.set(null);
          this.fail(AttachmentAction.Download, failure);
        },
      });
  }

  /**
   * Removes a row: a picked file that failed is only dropped from the list; a linked attachment is removed on the
   * server, then the list is loaded again (the server decides whether its type is missing again).
   */
  remove(rowId: string): void {
    if (isUploadRowId(rowId)) {
      this.uploads.update((rows) => rows.filter((row) => row.id !== rowId));
      return;
    }
    const attachment = this.findAttachment(rowId);
    if (!attachment || this.removingId() !== null) return;
    this.removingId.set(attachment.id);
    this.adapter
      .remove(attachment.id)
      .pipe(takeUntil(this.recordChanged))
      .subscribe({
        complete: () => {
          this.removingId.set(null);
          this.attachments.update((items) => items.filter((item) => item.id !== attachment.id));
          this.feedback.set(successFeedback(Keys.Removed, attachment.fileName));
          this.refresh();
        },
        error: (failure: unknown) => {
          this.removingId.set(null);
          this.fail(AttachmentAction.Remove, failure);
        },
      });
  }

  dismissFeedback(): void {
    this.feedback.set(null);
  }

  private uploadOne(record: RecordRef, row: UploadRow, file: File): Observable<AttachmentUploadEvent> {
    return this.adapter.upload(record.recordType, record.recordId, row.typeKey, file).pipe(
      tap((event) => {
        if (event.kind === AttachmentUploadEventKind.Progress) {
          this.patchUpload(row.id, (current) => uploadingUpload(current, event.totalBytes === null ? null : event.percent));
        } else {
          this.linked(row, event.attachment);
        }
      }),
      catchError((failure: unknown) => {
        const error = asAttachmentError(failure);
        this.patchUpload(row.id, (current) => failedUpload(current, error));
        this.fail(AttachmentAction.Upload, error);
        return EMPTY;
      }),
    );
  }

  private linked(row: UploadRow, attachment: RecordAttachment): void {
    this.uploads.update((rows) => rows.filter((candidate) => candidate.id !== row.id));
    this.attachments.update((items) => [...items, attachment]);
    this.feedback.set(successFeedback(Keys.Uploaded, attachment.fileName));
    this.refresh();
  }

  /** Reloads the list quietly (the screen keeps showing the current one); a failure is reported as a load failure. */
  private refresh(): void {
    const record = this.record;
    if (!record) return;
    this.adapter
      .list(record.recordType, record.recordId)
      .pipe(takeUntil(this.recordChanged))
      .subscribe({
        next: (list) => {
          this.attachments.set(list.items);
          this.missingTypeKeys.set(list.missingRequiredTypeKeys);
        },
        error: (failure: unknown) => this.fail(AttachmentAction.Load, failure),
      });
  }

  private patchUpload(id: string, change: (row: UploadRow) => UploadRow): void {
    this.uploads.update((rows) => rows.map((row) => (row.id === id ? change(row) : row)));
  }

  private fail(action: AttachmentAction, failure: unknown): void {
    this.feedback.set(failureFeedback(action, asAttachmentError(failure), (key) => this.translations.has(key)));
  }

  private findAttachment(id: string): RecordAttachment | undefined {
    return this.attachments().find((attachment) => attachment.id === id);
  }

  private nextUploadId(): string {
    this.uploadSequence += 1;
    return uploadRowId(this.uploadSequence);
  }
}
