import { describe, expect, it } from 'vitest';
import { DGA_ICON_GLYPHS } from '../icon/dga-icon-names.generated';
import { clampProgress, DGA_FILE_UPLOAD_STATUS_NAMES, DgaFileUploadStatus, fileLabel, fileStatusIcon, fileStatusName } from './file-upload-item';

describe('DgaFileUploadStatus', () => {
  it('is an integer enum with explicit, stable values', () => {
    expect(DgaFileUploadStatus).toEqual({ Queued: 0, Uploading: 1, Uploaded: 2, Failed: 3 });
  });
});

describe('fileStatusIcon', () => {
  it.each(Object.values(DgaFileUploadStatus))('gives %s an icon from the icon font', (status) => {
    expect(DGA_ICON_GLYPHS[fileStatusIcon(status)]).toBeDefined();
  });
});

describe('fileStatusName', () => {
  it('names every status for the data-status hook, each name distinct', () => {
    expect(Object.values(DgaFileUploadStatus).map(fileStatusName)).toEqual(['queued', 'uploading', 'uploaded', 'failed']);
    expect(new Set(Object.values(DGA_FILE_UPLOAD_STATUS_NAMES)).size).toBe(Object.values(DgaFileUploadStatus).length);
  });
});

describe('clampProgress', () => {
  it('clamps to 0..100 and rounds', () => {
    expect(clampProgress(-5)).toBe(0);
    expect(clampProgress(0)).toBe(0);
    expect(clampProgress(42.6)).toBe(43);
    expect(clampProgress(100)).toBe(100);
    expect(clampProgress(250)).toBe(100);
  });

  it('is null when unknown', () => {
    expect(clampProgress(null)).toBeNull();
    expect(clampProgress(undefined)).toBeNull();
    expect(clampProgress(Number.NaN)).toBeNull();
  });
});

describe('fileLabel', () => {
  it('fills every {name} placeholder', () => {
    expect(fileLabel('Remove {name}', 'scan.pdf')).toBe('Remove scan.pdf');
    expect(fileLabel('{name}: {name}', 'a')).toBe('a: a');
  });

  it('appends the name to a template without a placeholder', () => {
    expect(fileLabel('Remove', 'scan.pdf')).toBe('Remove scan.pdf');
  });
});
