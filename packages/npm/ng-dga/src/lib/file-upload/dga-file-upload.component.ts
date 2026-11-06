import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  input,
  numberAttribute,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { DgaButtonComponent } from '../button/dga-button.component';
import { DgaIconComponent } from '../icon/dga-icon.component';
import type { DgaIconName } from '../icon/dga-icon-names.generated';
import { hasText, joinIds } from '../internal/aria';
import { createDgaId } from '../internal/unique-id';
import { optionalNumberAttribute } from '../text-input/dga-text-input.component';
import {
  clampProgress,
  DGA_FILE_UPLOAD_PROGRESS_MAX,
  type DgaFileUploadItem,
  DgaFileUploadStatus,
  fileLabel,
  fileStatusIcon,
  fileStatusName,
} from './file-upload-item';
import { type DgaFileSelection, readFiles, validateFiles } from './file-validation';

/** The default number of files a control takes. */
export const DGA_FILE_UPLOAD_DEFAULT_MAX_FILES = 1;

/** The translated status announcement per status, each a template that may use `{name}`; a missing status says nothing. */
export type DgaFileUploadStatusLabels = Readonly<Partial<Record<DgaFileUploadStatus, string>>>;

/** One file row as the template renders it. */
export interface DgaFileUploadRow {
  readonly item: DgaFileUploadItem;
  readonly nameId: string;
  readonly errorId: string;
  readonly icon: DgaIconName;
  readonly statusName: string;
  readonly uploading: boolean;
  readonly failed: boolean;
  readonly progress: number | null;
  readonly statusText: string | null;
  readonly errorText: string | null;
  readonly referenceCode: string | null;
  readonly downloadLabel: string | null;
  readonly removeLabel: string | null;
}

/** The text when it is present and not empty, otherwise null. */
function textOrNull(text: string | null | undefined): string | null {
  return hasText(text) ? text : null;
}

/**
 * The DGA file upload, PRESENTATIONAL: a drop zone with a browse button, client-side pre-checks, and the list of files
 * the caller is uploading. It sends nothing; an app adapter does the HTTP and feeds the list back through `items`.
 *
 * - Picking (the browse button opens the browser's file dialog through a hidden native input) or dropping files
 *   emits `filesSelected` with `{ accepted, rejected }`: each rejection has a `DgaFileRejectionReason` (type, size,
 *   empty, count) for the caller to translate. A dialog closed without a file emits an empty selection.
 * - Pre-checks: `accept` (extensions, media types, `type/*`) and `maxFiles` (counting the rows already in `items`;
 *   more than one allows multi-select). The size check is OPTIONAL: set `maxSizeBytes` to refuse large files before
 *   sending; leave it null and the server decides, so an oversized file comes back as a failed row with its reference.
 * - `items` lists the files with their status (`DgaFileUploadStatus`: queued, uploading with a progress bar, uploaded,
 *   failed), detail, error text and reference code (labelled by `referenceCodeLabel`). `statusLabels` gives each status
 *   a translated announcement, read out through a polite live region. With a `removeLabel` (a template with `{name}`)
 *   each row has a named remove button that emits `remove`; with `downloadable` and a `downloadLabel` each uploaded row
 *   has a named download button that emits `download`.
 * - `required` marks the control; while it holds no uploaded file, `missingText` says so. `readonly` hides the drop
 *   zone and the remove buttons but keeps the list and the downloads.
 * - Name the control with a visible `label`, or point `ariaLabelledBy` at the caller's own label. `hint`, `error` and
 *   the missing text describe it; every text arrives translated. Extra content projects into the zone.
 *
 * @example
 * <dga-file-upload [label]="'attachments.documents' | t" [browseLabel]="'attachments.browse' | t" accept=".pdf,.png"
 *   maxFiles="5" required [missingText]="'attachments.missing' | t" [items]="rows()" [statusLabels]="statusLabels()"
 *   [referenceCodeLabel]="'attachments.referenceCode' | t" [removeLabel]="'attachments.remove' | t" downloadable
 *   [downloadLabel]="'attachments.download' | t" (filesSelected)="adapter.upload($event)"
 *   (download)="adapter.download($event)" (remove)="adapter.remove($event)" />
 */
@Component({
  selector: 'dga-file-upload',
  imports: [DgaButtonComponent, DgaIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-file-upload.component.html',
  styleUrl: './dga-file-upload.component.css',
})
export class DgaFileUploadComponent {
  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');

  /** Visible label of the control (already translated). */
  readonly label = input<string | null>(null);
  /** Id of the caller's own label element, used when there is no `label`. */
  readonly ariaLabelledBy = input<string | null>(null);
  readonly hint = input<string | null>(null);
  /** The prompt inside the drop zone, such as "Drag files here or" (already translated). */
  readonly dropLabel = input<string | null>(null);
  readonly browseLabel = input.required<string>();
  /** The native accept list: extensions, media types and `type/*` wildcards, comma-separated. Empty accepts any type. */
  readonly accept = input('');
  /** OPTIONAL client size pre-check in bytes; null (the default) leaves the size to the server, which is authoritative. */
  readonly maxSizeBytes = input<number | null, unknown>(null, { transform: optionalNumberAttribute });
  /** How many files the control holds at most, counting `items`. */
  readonly maxFiles = input(DGA_FILE_UPLOAD_DEFAULT_MAX_FILES, {
    transform: (value: unknown) => numberAttribute(value, DGA_FILE_UPLOAD_DEFAULT_MAX_FILES),
  });
  readonly disabled = input(false, { transform: booleanAttribute });
  /** Shows the list only: no drop zone and no remove buttons; downloads stay. */
  readonly readonly = input(false, { transform: booleanAttribute });
  /** Marks the control as required (a visual marker; `requiredLabel` names it for screen readers). */
  readonly required = input(false, { transform: booleanAttribute });
  /** The screen-reader word for the required marker, such as "required" (already translated). */
  readonly requiredLabel = input<string | null>(null);
  /** Shown, and describing the control, while it is required and holds no uploaded file (already translated). */
  readonly missingText = input<string | null>(null);
  /** The current problem (already translated), such as a rejection the caller translated; shown as an alert. */
  readonly error = input<string | null>(null);
  /** The files the caller holds, with their upload status. */
  readonly items = input<readonly DgaFileUploadItem[]>([]);
  /** The announcement per status (templates with `{name}`, already translated), read through a polite live region. */
  readonly statusLabels = input<DgaFileUploadStatusLabels>({});
  /** The label before a failed row's reference code, such as "Reference code" (already translated). */
  readonly referenceCodeLabel = input<string | null>(null);
  /** Uploaded rows offer a download button (needs `downloadLabel`). */
  readonly downloadable = input(false, { transform: booleanAttribute });
  /** The download button's accessible name, a template with `{name}` (already translated). */
  readonly downloadLabel = input<string | null>(null);
  /** The remove button's accessible name, a template with `{name}` (already translated); without it rows cannot be removed. */
  readonly removeLabel = input<string | null>(null);
  readonly filesSelected = output<DgaFileSelection>();
  /** The row whose download button was pressed. */
  readonly download = output<DgaFileUploadItem>();
  /** The row whose remove button was pressed. */
  readonly remove = output<DgaFileUploadItem>();

  readonly uploadId = createDgaId('dga-file-upload');
  readonly labelId = `${this.uploadId}-label`;
  readonly hintId = `${this.uploadId}-hint`;
  readonly errorId = `${this.uploadId}-error`;
  readonly missingId = `${this.uploadId}-missing`;
  readonly progressMax = DGA_FILE_UPLOAD_PROGRESS_MAX;
  readonly dragging = signal(false);

  readonly hasLabel = computed(() => hasText(this.label()));
  readonly hasHint = computed(() => hasText(this.hint()));
  readonly hasError = computed(() => hasText(this.error()));
  readonly missing = computed(
    () => this.required() && hasText(this.missingText()) && !this.items().some((item) => item.status === DgaFileUploadStatus.Uploaded),
  );
  readonly multiple = computed(() => this.maxFiles() > DGA_FILE_UPLOAD_DEFAULT_MAX_FILES);
  readonly labelledBy = computed(() => (this.hasLabel() ? this.labelId : this.ariaLabelledBy()));
  readonly describedBy = computed(() =>
    joinIds([this.hasHint() ? this.hintId : null, this.missing() ? this.missingId : null, this.hasError() ? this.errorId : null]),
  );
  readonly rows = computed<DgaFileUploadRow[]>(() => this.items().map((item, index) => this.toRow(item, index)));

  /** Opens the browser's file dialog (nothing while disabled or read-only). */
  browse(): void {
    if (this.canPick()) this.fileInput()?.nativeElement.click();
  }

  /** Forgets the native input's file, so picking the same file again is a change. Emits nothing. */
  clearInput(): void {
    const element = this.fileInput()?.nativeElement;
    if (element) element.value = '';
  }

  onPicked(event: Event): void {
    const element = event.target as HTMLInputElement | null;
    this.select(readFiles(element?.files));
    if (element) element.value = '';
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(this.canPick());
  }

  onDragLeave(): void {
    this.dragging.set(false);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    if (this.canPick()) this.select(readFiles(event.dataTransfer?.files));
  }

  /** Emits `download` for an uploaded row; allowed while disabled or read-only, since it changes nothing. */
  onDownload(item: DgaFileUploadItem): void {
    if (this.downloadable() && item.status === DgaFileUploadStatus.Uploaded) this.download.emit(item);
  }

  onRemove(item: DgaFileUploadItem): void {
    if (this.canPick()) this.remove.emit(item);
  }

  private canPick(): boolean {
    return !this.disabled() && !this.readonly();
  }

  private select(files: readonly File[]): void {
    const remainingSlots = Math.max(0, this.maxFiles() - this.items().length);
    this.filesSelected.emit(validateFiles(files, { accept: this.accept(), maxSizeBytes: this.maxSizeBytes(), remainingSlots }));
  }

  private toRow(item: DgaFileUploadItem, index: number): DgaFileUploadRow {
    const prefix = `${this.uploadId}-file-${index}`;
    const failed = item.status === DgaFileUploadStatus.Failed;
    return {
      item,
      nameId: `${prefix}-name`,
      errorId: `${prefix}-error`,
      icon: fileStatusIcon(item.status),
      statusName: fileStatusName(item.status),
      uploading: item.status === DgaFileUploadStatus.Uploading,
      failed,
      progress: clampProgress(item.progress),
      statusText: this.fileText(this.statusLabels()[item.status], item.name),
      errorText: failed ? textOrNull(item.errorText) : null,
      referenceCode: failed ? textOrNull(item.referenceCode) : null,
      downloadLabel:
        this.downloadable() && item.status === DgaFileUploadStatus.Uploaded ? this.fileText(this.downloadLabel(), item.name) : null,
      removeLabel: this.readonly() ? null : this.fileText(this.removeLabel(), item.name),
    };
  }

  private fileText(template: string | null | undefined, name: string): string | null {
    return hasText(template) ? fileLabel(template, name) : null;
  }
}
