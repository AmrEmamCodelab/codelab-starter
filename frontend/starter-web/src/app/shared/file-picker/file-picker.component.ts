import {
  type AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  booleanAttribute,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { DgaFileUploadComponent, type DgaFileSelection } from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { FilePickerTestIds, FilePickerTranslationKeys } from './file-picker-keys';

const NATIVE_FILE_INPUT_SELECTOR = 'input[type="file"]';
const TEST_ID_ATTRIBUTE = 'data-testid';

/**
 * The file a single-file pick yields: the accepted one, else the one the client-side checks refused (the screen's
 * own rules decide what to say about it), else null when the dialog was left empty.
 */
export function pickedFile(selection: DgaFileSelection): File | null {
  return selection.accepted[0] ?? selection.rejected[0]?.file ?? null;
}

/**
 * A translated single-file picker on the ng-dga `dga-file-upload`: its button ("Choose file" in the current
 * language) opens the browser's file dialog, a file can also be dropped on it, and the chosen file's name (or a
 * translated "No file chosen") shows inside it. The native control's own English text never renders. Emits
 * `fileChange` with the picked file, or null when the dialog is left empty; `clear()` forgets the file.
 *
 * The screen owns the file rules: a file outside `accept` is still emitted, so the screen's store can name the
 * problem in its own words (`dga-file-upload`'s rejection reasons are not shown here).
 */
@Component({
  selector: 'app-file-picker',
  imports: [DgaFileUploadComponent, TranslatePipe],
  templateUrl: './file-picker.component.html',
  styleUrl: './file-picker.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FilePickerComponent implements AfterViewInit {
  private readonly upload = viewChild.required(DgaFileUploadComponent);
  private readonly uploadHost = viewChild.required(DgaFileUploadComponent, { read: ElementRef });

  /** Translation key of the visible label. */
  readonly labelKey = input.required<string>();
  /** The native `accept` list: extensions and media types, comma-separated. */
  readonly accept = input('');
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly fileChange = output<File | null>();

  protected readonly keys = FilePickerTranslationKeys;
  protected readonly testIds = FilePickerTestIds;
  protected readonly fileName = signal<string | null>(null);
  protected readonly labelId = `app-file-picker-${nextPickerId()}`;

  /** Keeps the stable test hook on the native input (screen specs and Playwright pick files through it). */
  ngAfterViewInit(): void {
    const host = this.uploadHost().nativeElement as HTMLElement;
    host.querySelector(NATIVE_FILE_INPUT_SELECTOR)?.setAttribute(TEST_ID_ATTRIBUTE, FilePickerTestIds.Input);
  }

  /** Opens the browser's file dialog (nothing while disabled). */
  choose(): void {
    this.upload().browse();
  }

  /** Forgets the picked file, so picking the same file again is a change. Emits nothing. */
  clear(): void {
    this.upload().clearInput();
    this.fileName.set(null);
  }

  protected picked(selection: DgaFileSelection): void {
    const file = pickedFile(selection);
    this.fileName.set(file?.name ?? null);
    this.fileChange.emit(file);
  }
}

let pickerCount = 0;

function nextPickerId(): number {
  pickerCount += 1;
  return pickerCount;
}
