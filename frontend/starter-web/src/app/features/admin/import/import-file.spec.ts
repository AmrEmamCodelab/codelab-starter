import { describe, expect, it } from 'vitest';
import {
  IMPORT_FILE_MEDIA_TYPE,
  IMPORT_MAX_UPLOAD_BYTES,
  ImportFileProblem,
  importFileProblem,
} from './import-file';

function workbook(name: string, size: number, type = IMPORT_FILE_MEDIA_TYPE): File {
  const file = new File(['x'], name, { type });
  Object.defineProperty(file, 'size', { value: size });
  return file;
}

describe('importFileProblem', () => {
  it('accepts an .xlsx within the cap, with or without a media type', () => {
    expect(importFileProblem(workbook('users.xlsx', IMPORT_MAX_UPLOAD_BYTES))).toBeNull();
    expect(importFileProblem(workbook('USERS.XLSX', 10, ''))).toBeNull();
  });

  it('refuses another extension or another media type', () => {
    expect(importFileProblem(workbook('users.xls', 10))).toBe(ImportFileProblem.WrongType);
    expect(importFileProblem(workbook('users.csv', 10, 'text/csv'))).toBe(ImportFileProblem.WrongType);
    expect(importFileProblem(workbook('users.xlsx', 10, 'text/csv'))).toBe(ImportFileProblem.WrongType);
  });

  it('refuses an empty file and one over the cap', () => {
    expect(importFileProblem(workbook('users.xlsx', 0))).toBe(ImportFileProblem.Empty);
    expect(importFileProblem(workbook('users.xlsx', IMPORT_MAX_UPLOAD_BYTES + 1))).toBe(ImportFileProblem.TooLarge);
  });

  it('caps uploads at 5 MiB like the server', () => {
    expect(IMPORT_MAX_UPLOAD_BYTES).toBe(5 * 1024 * 1024);
  });
});
