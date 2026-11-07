import type { DgaTagTone } from '@codelab/ng-dga';
import { referenceCodeFrom } from '@codelab/ng-security';

/** Translation keys of the Reference lookup screen. */
export const ReferenceKeys = {
  Title: 'reference.title',
  Subtitle: 'reference.subtitle',
  Label: 'reference.label',
  Hint: 'reference.hint',
  Invalid: 'reference.invalid',
  Search: 'reference.search',
  /** `{{code}}`. */
  NotFound: 'reference.notFound',
  Request: 'reference.request',
  Events: 'reference.events',
  EventsCaption: 'reference.eventsCaption',
  NoEvents: 'reference.noEvents',
  Fields: {
    ReferenceCode: 'reference.fields.referenceCode',
    CorrelationId: 'reference.fields.correlationId',
    TraceId: 'reference.fields.traceId',
    FirstSeen: 'reference.fields.firstSeen',
    Method: 'reference.fields.method',
    Path: 'reference.fields.path',
    Status: 'reference.fields.status',
  },
  Columns: {
    Timestamp: 'reference.columns.timestamp',
    Level: 'reference.columns.level',
    Message: 'reference.columns.message',
  },
  Seq: {
    Open: 'reference.seq.open',
    Copy: 'reference.seq.copy',
    Copied: 'reference.seq.copied',
    CopyFailed: 'reference.seq.copyFailed',
    Unavailable: 'reference.seq.unavailable',
  },
} as const;

/** The longest text the lookup box accepts: a correlation id written as a GUID, with room for spaces. */
export const REFERENCE_INPUT_MAX_LENGTH = 64;

const GROUP_LENGTH = 5;
const GROUP_SEPARATOR = '-';
const SEPARATORS = /[\s-]+/g;
const SHORT_CODE = /^[0-9A-F]{10}$/;
const CORRELATION_ID = /^[0-9A-Fa-f]{32}$/;

/**
 * The canonical reference code (`ABCDE-12345`, the format Codelab.Observability writes) for what an operator typed,
 * or null when it cannot be one. Accepts the code in any case, with or without the dash and with stray spaces, and a
 * full 32-hex correlation id (also as a dashed GUID), which is shortened the way the server shortens it.
 */
export function normalizeReferenceCode(input: string): string | null {
  const compact = input.replace(SEPARATORS, '');
  if (CORRELATION_ID.test(compact)) {
    return referenceCodeFrom(compact);
  }
  const upper = compact.toUpperCase();
  if (!SHORT_CODE.test(upper)) {
    return null;
  }
  return `${upper.slice(0, GROUP_LENGTH)}${GROUP_SEPARATOR}${upper.slice(GROUP_LENGTH)}`;
}

/** Seq's level names, as the BFF passes them through. */
export const SeqLevels = {
  Verbose: 'Verbose',
  Debug: 'Debug',
  Information: 'Information',
  Warning: 'Warning',
  Error: 'Error',
  Fatal: 'Fatal',
} as const;

const LEVEL_TONES: ReadonlyMap<string, DgaTagTone> = new Map<string, DgaTagTone>([
  [SeqLevels.Verbose, 'neutral'],
  [SeqLevels.Debug, 'neutral'],
  [SeqLevels.Information, 'info'],
  [SeqLevels.Warning, 'warning'],
  [SeqLevels.Error, 'error'],
  [SeqLevels.Fatal, 'error'],
]);

/** The tag tone of a log level; an unknown level is neutral. */
export function levelTone(level: string): DgaTagTone {
  return LEVEL_TONES.get(level) ?? 'neutral';
}

const WEB_PROTOCOLS: ReadonlySet<string> = new Set(['http:', 'https:']);

/** `url` when it is an absolute http(s) URL, else null: only a web link is offered as "Open in Seq". */
export function webUrlOrNull(url: string | null | undefined): string | null {
  if (!url) {
    return null;
  }
  try {
    return WEB_PROTOCOLS.has(new URL(url).protocol) ? url : null;
  } catch {
    return null;
  }
}
