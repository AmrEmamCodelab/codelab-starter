import { describe, expect, it } from 'vitest';
import { acceptEntries, DgaFileRejectionReason, fileRejectionReason, matchesAccept, readFiles, validateFiles } from './file-validation';

const PDF_TYPE = 'application/pdf';
const KIB = 1024;

function file(name: string, options: { type?: string; size?: number } = {}): File {
  return new File(['x'.repeat(options.size ?? 1)], name, { type: options.type ?? '' });
}

describe('DgaFileRejectionReason', () => {
  it('is an integer enum with explicit, stable values', () => {
    expect(DgaFileRejectionReason).toEqual({ Type: 0, Size: 1, Empty: 2, Count: 3 });
  });
});

describe('acceptEntries', () => {
  it('trims, lower-cases and drops empty entries', () => {
    expect(acceptEntries(' .PDF, image/* ,,')).toEqual(['.pdf', 'image/*']);
    expect(acceptEntries('')).toEqual([]);
  });
});

describe('matchesAccept', () => {
  it('accepts every file for an empty list', () => {
    expect(matchesAccept(file('a.exe'), '')).toBe(true);
  });

  it('matches by extension, case-insensitively', () => {
    expect(matchesAccept(file('Report.PDF'), '.pdf')).toBe(true);
    expect(matchesAccept(file('report.docx'), '.pdf,.png')).toBe(false);
  });

  it('matches an exact media type and a wildcard', () => {
    expect(matchesAccept(file('scan', { type: PDF_TYPE }), PDF_TYPE)).toBe(true);
    expect(matchesAccept(file('photo', { type: 'image/png' }), 'image/*')).toBe(true);
    expect(matchesAccept(file('clip', { type: 'video/mp4' }), 'image/*')).toBe(false);
  });

  it('cannot match a file without a media type by type', () => {
    expect(matchesAccept(file('photo'), 'image/*')).toBe(false);
    expect(matchesAccept(file('scan'), PDF_TYPE)).toBe(false);
  });
});

describe('fileRejectionReason', () => {
  const rules = { accept: '.pdf', maxSizeBytes: KIB };

  it('checks the type first, then emptiness, then the size', () => {
    expect(fileRejectionReason(file('a.png', { size: 0 }), rules)).toBe(DgaFileRejectionReason.Type);
    expect(fileRejectionReason(file('a.pdf', { size: 0 }), rules)).toBe(DgaFileRejectionReason.Empty);
    expect(fileRejectionReason(file('a.pdf', { size: KIB + 1 }), rules)).toBe(DgaFileRejectionReason.Size);
    expect(fileRejectionReason(file('a.pdf', { size: KIB }), rules)).toBeNull();
  });

  it('applies the optional size check only when maxSizeBytes is set', () => {
    const big = file('a.pdf', { size: KIB + 1 });
    expect(fileRejectionReason(big, { accept: '.pdf', maxSizeBytes: KIB })).toBe(DgaFileRejectionReason.Size);
    expect(fileRejectionReason(big, { accept: '.pdf', maxSizeBytes: null })).toBeNull();
  });

  it('has no size limit when maxSizeBytes is null', () => {
    expect(fileRejectionReason(file('a.pdf', { size: KIB * KIB }), { accept: '', maxSizeBytes: null })).toBeNull();
  });
});

describe('validateFiles', () => {
  it('keeps the order and refuses valid files past the free slots as count', () => {
    const [first, wrong, second, third] = [file('1.pdf'), file('2.png'), file('3.pdf'), file('4.pdf')];
    const selection = validateFiles([first, wrong, second, third], { accept: '.pdf', maxSizeBytes: null, remainingSlots: 2 });
    expect(selection.accepted).toEqual([first, second]);
    expect(selection.rejected).toEqual([
      { file: wrong, reason: DgaFileRejectionReason.Type },
      { file: third, reason: DgaFileRejectionReason.Count },
    ]);
  });

  it('refuses everything as count when no slot is free', () => {
    const only = file('1.pdf');
    expect(validateFiles([only], { accept: '', maxSizeBytes: null, remainingSlots: 0 })).toEqual({
      accepted: [],
      rejected: [{ file: only, reason: DgaFileRejectionReason.Count }],
    });
  });

  it('returns an empty selection for no files', () => {
    expect(validateFiles([], { accept: '', maxSizeBytes: null, remainingSlots: 1 })).toEqual({ accepted: [], rejected: [] });
  });
});

describe('readFiles', () => {
  it('reads a source with a length', () => {
    const files = [file('a'), file('b')];
    expect(readFiles({ length: files.length, item: (index) => files[index] ?? null })).toEqual(files);
  });

  it('reads a source without a length up to the first null', () => {
    const only = file('a');
    expect(readFiles({ item: (index) => (index === 0 ? only : null) })).toEqual([only]);
  });

  it('returns nothing for a missing source', () => {
    expect(readFiles(null)).toEqual([]);
    expect(readFiles(undefined)).toEqual([]);
  });
});
