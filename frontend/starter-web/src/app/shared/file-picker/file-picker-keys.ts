/** The texts of `app-file-picker`, under `shared.file-picker.*` in both bundles. */
export const FilePickerTranslationKeys = {
  Choose: 'shared.file-picker.choose',
  None: 'shared.file-picker.none',
} as const;

/** The stable `data-testid` hooks of `app-file-picker` (screen specs and Playwright use them). */
export const FilePickerTestIds = {
  /** The hidden native `<input type="file">`. */
  Input: 'file-picker-input',
  /** The upload control; its first `button` is the choose button. */
  Choose: 'file-picker-choose',
  /** The chosen file's name, or the "no file" text. */
  Name: 'file-picker-name',
} as const;
