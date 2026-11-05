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
} from './file-upload-item';
import { type DgaFileSelection, readFiles, validateFiles } from './file-validation';

/** The default number of files a control takes. */
export const DGA_FILE_UPLOAD_DEFAULT_MAX_FILES = 1;

/** One file row as the template renders it. */
export interface DgaFileUploadRow {
  readonly item: DgaFileUploadItem;
  readonly nameId: string;
  readonly errorId: string;
  readonly icon: DgaIconName;
  readonly uploading: boolean;
  readonly failed: boolean;
  readonly progress: number | null;
  readonly removeLabel: string | null;
}

/**
 * The DGA file upload, PRESENTATIONAL: a drop zone with a browse button, client-side checks, and the list of files
 * the caller is uploading. It sends nothing; an app adapter does the HTTP and feeds the list back through `items`.
 *
 * - Picking (the browse button opens the browser's file dialog through a hidden native input) or dropping files
 *   emits `filesSelected` with `{ accepted, rejected }`: each rejection has a `DgaFileRejectionReason` (type, size,
 *   empty, count) for the caller to translate. A dialog closed without a file emits an empty selection.
 * - Rules: `accept` (extensions, media types, `type/*`), `maxSizeBytes` (null: no limit) and `maxFiles` (counting the
 *   rows already in `items`; more than one allows multi-select).
 * - `items` lists the files with their status, progress bar (percent, or indeterminate), detail and error; with a
 *   `removeLabel` (a template with `{name}`) each row has a named remove button that emits `removed` with its id.
 * - Name the control with a visible `label`, or point `ariaLabelledBy` at the caller's own label. `hint` and `error`
 *   describe it; every text arrives translated. Extra content (the chosen file's name, a note) projects into the zone.
 *
 * @example
 * <dga-file-upload [label]="'contact.attachment' | t" [browseLabel]="'common.browse' | t" accept=".pdf,image/*"
 *   [maxSizeBytes]="maxBytes" maxFiles="3" [items]="uploads()" [removeLabel]="'common.removeFile' | t"
 *   [error]="problem() | t" (filesSelected)="adapter.add($event)" (removed)="adapter.remove($event)" />
 */
@Component({
  selector: 'dga-file-upload',
  imports: [DgaButtonComponent, DgaIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-file-upload.component.html',
  styleUrl: './dga-file-upload.component.css',
})
export class DgaFileUploadComponent {
  private readonly fileInput = viewChild.required<ElementRef<HTMLInputElement>>('fileInput');

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
  /** The largest file in bytes; null for no limit. */
  readonly maxSizeBytes = input<number | null, unknown>(null, { transform: optionalNumberAttribute });
  /** How many files the control holds at most, counting `items`. */
  readonly maxFiles = input(DGA_FILE_UPLOAD_DEFAULT_MAX_FILES, {
    transform: (value: unknown) => numberAttribute(value, DGA_FILE_UPLOAD_DEFAULT_MAX_FILES),
  });
  readonly disabled = input(false, { transform: booleanAttribute });
  /** The current problem (already translated), such as a rejection the caller translated; shown as an alert. */
  readonly error = input<string | null>(null);
  /** The files the caller holds, with their upload status. */
  readonly items = input<readonly DgaFileUploadItem[]>([]);
  /** The remove button's accessible name, a template with `{name}` (already translated); without it rows cannot be removed. */
  readonly removeLabel = input<string | null>(null);
  readonly filesSelected = output<DgaFileSelection>();
  readonly removed = output<string>();

  readonly uploadId = createDgaId('dga-file-upload');
  readonly labelId = `${this.uploadId}-label`;
  readonly hintId = `${this.uploadId}-hint`;
  readonly errorId = `${this.uploadId}-error`;
  readonly progressMax = DGA_FILE_UPLOAD_PROGRESS_MAX;
  readonly dragging = signal(false);

  readonly hasLabel = computed(() => hasText(this.label()));
  readonly hasHint = computed(() => hasText(this.hint()));
  readonly hasError = computed(() => hasText(this.error()));
  readonly multiple = computed(() => this.maxFiles() > DGA_FILE_UPLOAD_DEFAULT_MAX_FILES);
  readonly labelledBy = computed(() => (this.hasLabel() ? this.labelId : this.ariaLabelledBy()));
  readonly describedBy = computed(() => joinIds([this.hasHint() ? this.hintId : null, this.hasError() ? this.errorId : null]));
  readonly rows = computed<DgaFileUploadRow[]>(() => {
    const removeTemplate = this.removeLabel();
    return this.items().map((item, index) => ({
      item,
      nameId: `${this.uploadId}-file-${index}`,
      errorId: `${this.uploadId}-file-${index}-error`,
      icon: fileStatusIcon(item.status),
      uploading: item.status === DgaFileUploadStatus.Uploading,
      failed: item.status === DgaFileUploadStatus.Failed,
      progress: clampProgress(item.progress),
      removeLabel: hasText(removeTemplate) ? fileLabel(removeTemplate, item.name) : null,
    }));
  });

  /** Opens the browser's file dialog (nothing while disabled). */
  browse(): void {
    if (!this.disabled()) this.fileInput().nativeElement.click();
  }

  /** Forgets the native input's file, so picking the same file again is a change. Emits nothing. */
  clearInput(): void {
    this.fileInput().nativeElement.value = '';
  }

  onPicked(event: Event): void {
    const input = event.target as HTMLInputElement | null;
    this.select(readFiles(input?.files));
    if (input) input.value = '';
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(!this.disabled());
  }

  onDragLeave(): void {
    this.dragging.set(false);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    if (!this.disabled()) this.select(readFiles(event.dataTransfer?.files));
  }

  remove(id: string): void {
    if (!this.disabled()) this.removed.emit(id);
  }

  private select(files: readonly File[]): void {
    const remainingSlots = Math.max(0, this.maxFiles() - this.items().length);
    this.filesSelected.emit(validateFiles(files, { accept: this.accept(), maxSizeBytes: this.maxSizeBytes(), remainingSlots }));
  }
}
