import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  input,
  output,
  signal,
  viewChild,
  type ElementRef,
} from '@angular/core';
import { DgaButtonComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

/** The file a file `<input>` holds after its `change` event (the first one; the picker allows one), or null. */
export function firstPickedFile(event: Event): File | null {
  return (event.target as HTMLInputElement | null)?.files?.item(0) ?? null;
}

/**
 * A translated single-file picker: a DGA button ("Choose file" in the current language) opens the browser's file
 * dialog through a visually hidden native `<input type="file">`, and the chosen file's name (or a translated
 * "No file chosen") shows beside it. The native control's own English text never renders. Emits `fileChange` with
 * the picked file, or null when the dialog is left empty; `clear()` forgets the file.
 *
 * Interim home: this belongs in `@codelab/ng-dga` as `dga-file-input` (a platform-owner change); until it lands
 * there, screens use this one.
 */
@Component({
  selector: 'app-file-picker',
  imports: [DgaButtonComponent, TranslatePipe],
  templateUrl: './file-picker.component.html',
  styleUrl: './file-picker.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FilePickerComponent {
  private readonly input = viewChild.required<ElementRef<HTMLInputElement>>('fileInput');

  /** Translation key of the visible label. */
  readonly labelKey = input.required<string>();
  /** The native `accept` list: extensions and media types, comma-separated. */
  readonly accept = input('');
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly fileChange = output<File | null>();

  protected readonly fileName = signal<string | null>(null);
  protected readonly labelId = `app-file-picker-${nextPickerId()}`;

  /** Opens the browser's file dialog (nothing while disabled). */
  choose(): void {
    if (!this.disabled()) {
      this.input().nativeElement.click();
    }
  }

  /** Forgets the picked file, so picking the same file again is a change. Emits nothing. */
  clear(): void {
    this.input().nativeElement.value = '';
    this.fileName.set(null);
  }

  protected picked(event: Event): void {
    const file = firstPickedFile(event);
    this.fileName.set(file?.name ?? null);
    this.fileChange.emit(file);
  }
}

let pickerCount = 0;

function nextPickerId(): number {
  pickerCount += 1;
  return pickerCount;
}
