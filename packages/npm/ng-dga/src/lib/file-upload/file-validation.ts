/** Why <dga-file-upload> refused a picked or dropped file, before anything is uploaded. Integer values, explicit. */
export const DgaFileRejectionReason = {
  /** The file matches none of the `accept` entries. */
  Type: 0,
  /** The file is larger than `maxSizeBytes` (only when the caller opted into the client size check). */
  Size: 1,
  /** The file is empty (0 bytes). */
  Empty: 2,
  /** Accepting the file would exceed `maxFiles`. */
  Count: 3,
} as const;
export type DgaFileRejectionReason = (typeof DgaFileRejectionReason)[keyof typeof DgaFileRejectionReason];

export interface DgaFileRejection {
  readonly file: File;
  readonly reason: DgaFileRejectionReason;
}

/** The result of one pick or drop: the files that passed the client-side checks and the ones refused, with why. */
export interface DgaFileSelection {
  readonly accepted: readonly File[];
  readonly rejected: readonly DgaFileRejection[];
}

/** The client-side rules of one upload control. */
export interface DgaFileRules {
  /** The native `accept` list: extensions (`.pdf`), media types (`application/pdf`) and wildcards (`image/*`). Empty accepts any type. */
  readonly accept: string;
  /**
   * The largest file in bytes, an OPTIONAL client pre-check; null skips it and leaves the size to the server, which
   * is authoritative either way (an oversized file then comes back failed with the server's reference code).
   */
  readonly maxSizeBytes: number | null;
  /** How many more files the control may take now (its limit minus the files it already holds). */
  readonly remainingSlots: number;
}

/** Anything that lists files by index: a `FileList`, or a test double with `item()` (and maybe no `length`). */
export interface DgaFileSource {
  readonly length?: number;
  item(index: number): File | null;
}

const ACCEPT_SEPARATOR = ',';
const EXTENSION_PREFIX = '.';
const WILDCARD_SUFFIX = '/*';
const TYPE_SEPARATOR = '/';

/** The entries of an `accept` list, trimmed and lower-cased; empty entries dropped. */
export function acceptEntries(accept: string): string[] {
  return accept
    .split(ACCEPT_SEPARATOR)
    .map((entry) => entry.trim().toLowerCase())
    .filter((entry) => entry.length > 0);
}

/**
 * True when the file matches the `accept` list the way a browser's file dialog filters it: by name extension, exact
 * media type or a `type/*` wildcard. An empty list accepts every file. A file with no media type (browsers often send
 * none) can only match by extension.
 */
export function matchesAccept(file: Pick<File, 'name' | 'type'>, accept: string): boolean {
  const entries = acceptEntries(accept);
  if (entries.length === 0) return true;
  const name = file.name.toLowerCase();
  const type = file.type.toLowerCase();
  return entries.some((entry) => {
    if (entry.startsWith(EXTENSION_PREFIX)) return name.endsWith(entry);
    if (type === '') return false;
    if (entry.endsWith(WILDCARD_SUFFIX)) return type.startsWith(entry.slice(0, entry.length - WILDCARD_SUFFIX.length) + TYPE_SEPARATOR);
    return type === entry;
  });
}

/** Why one file is refused under the rules, ignoring the count; null when it passes. */
export function fileRejectionReason(file: Pick<File, 'name' | 'type' | 'size'>, rules: Omit<DgaFileRules, 'remainingSlots'>): DgaFileRejectionReason | null {
  if (!matchesAccept(file, rules.accept)) return DgaFileRejectionReason.Type;
  if (file.size === 0) return DgaFileRejectionReason.Empty;
  if (rules.maxSizeBytes !== null && file.size > rules.maxSizeBytes) return DgaFileRejectionReason.Size;
  return null;
}

/** Splits picked or dropped files into accepted and rejected, in their order; files past the free slots are refused as `count`. */
export function validateFiles(files: readonly File[], rules: DgaFileRules): DgaFileSelection {
  const accepted: File[] = [];
  const rejected: DgaFileRejection[] = [];
  for (const file of files) {
    const reason = fileRejectionReason(file, rules) ?? (accepted.length >= rules.remainingSlots ? DgaFileRejectionReason.Count : null);
    if (reason === null) accepted.push(file);
    else rejected.push({ file, reason });
  }
  return { accepted, rejected };
}

/**
 * The files of a file input or a drop, in order: the first `length` items, or, for a source without a length, the
 * items up to the first null.
 */
export function readFiles(source: DgaFileSource | null | undefined): File[] {
  const files: File[] = [];
  if (!source) return files;
  const count = source.length;
  for (let index = 0; count === undefined || index < count; index += 1) {
    const file = source.item(index);
    if (file === null) break;
    files.push(file);
  }
  return files;
}
