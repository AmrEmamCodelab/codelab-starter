const BYTES_PER_KIBIBYTE = 1024;
const BYTES_PER_MEBIBYTE = BYTES_PER_KIBIBYTE * BYTES_PER_KIBIBYTE;

/** The largest workbook the preview accepts, in mebibytes (the backend `ImportLimits.MaxUploadBytes`). */
export const IMPORT_MAX_UPLOAD_MEBIBYTES = 5;

export const IMPORT_MAX_UPLOAD_BYTES = IMPORT_MAX_UPLOAD_MEBIBYTES * BYTES_PER_MEBIBYTE;

/** The one file type the import reads: an Excel workbook (Office Open XML). */
export const IMPORT_FILE_EXTENSION = '.xlsx';

export const IMPORT_FILE_MEDIA_TYPE = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

/** The `accept` attribute of the file picker. */
export const IMPORT_FILE_ACCEPT = `${IMPORT_FILE_EXTENSION},${IMPORT_FILE_MEDIA_TYPE}`;

/** Why a picked file is refused before it is uploaded. */
export enum ImportFileProblem {
  WrongType = 1,
  Empty = 2,
  TooLarge = 3,
}

export const ImportFileProblemKeys: Readonly<Record<ImportFileProblem, string>> = {
  [ImportFileProblem.WrongType]: 'admin.import.file.wrong-type',
  [ImportFileProblem.Empty]: 'admin.import.file.empty',
  [ImportFileProblem.TooLarge]: 'admin.import.file.too-large',
};

/**
 * The client-side check of a picked workbook, so an obviously wrong file never travels: the name must end in .xlsx
 * (browsers often send no media type, so a missing type is accepted, but another type is refused), it must not be
 * empty and it must fit the upload cap. The server checks again and reads the content.
 */
export function importFileProblem(file: File): ImportFileProblem | null {
  const typeKnown = file.type !== '';
  if (!file.name.toLowerCase().endsWith(IMPORT_FILE_EXTENSION) || (typeKnown && file.type !== IMPORT_FILE_MEDIA_TYPE)) {
    return ImportFileProblem.WrongType;
  }
  if (file.size === 0) {
    return ImportFileProblem.Empty;
  }
  return file.size > IMPORT_MAX_UPLOAD_BYTES ? ImportFileProblem.TooLarge : null;
}
