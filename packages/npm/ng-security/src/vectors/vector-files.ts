/**
 * Loads the golden vectors for the TypeScript runner. Spec support only: excluded from the build.
 *
 * By default the runner reads the checked-in files in `packages/vectors` with the pinned prefix
 * `CODELABTEST`. `tools/vectors/verify-vectors.mjs` points it at another set with
 * `CODELAB_VECTORS_DIR` + `CODELAB_VECTOR_PREFIX` (a set the C# generator wrote for that prefix).
 * The fixture keys always come from `packages/vectors/fixture-keys`: every set is generated from them.
 */

import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { CryptoBytes } from '../canonical/canonical.ts';
import { TEST_PROTOCOL_PREFIX } from '../protocol/security-protocol.ts';

export const VECTORS_DIR_VARIABLE = 'CODELAB_VECTORS_DIR';
export const VECTOR_PREFIX_VARIABLE = 'CODELAB_VECTOR_PREFIX';
export const SCHEMA_VERSION = 1;
export const EXPECTED_PREFIX = 'expected';

/** The vector areas, one file each (`<area>.vectors.json`). */
export const VectorAreas = Object.freeze({
  canonical: 'canonical',
  sessionKeys: 'session-keys',
  requestSignature: 'request-signature',
  responseSignature: 'response-signature',
  publicId: 'public-id',
} as const);

export type VectorArea = (typeof VectorAreas)[keyof typeof VectorAreas];

const FILE_SUFFIX = '.vectors.json';
const CHECKED_IN_DIRECTORY = fileURLToPath(new URL('../../../../vectors/', import.meta.url));
const FIXTURE_KEYS_DIRECTORY = join(CHECKED_IN_DIRECTORY, 'fixture-keys');
const UTF8 = 'utf8';

/** Where a run reads its vectors from, and the prefix they must carry. */
export interface VectorSource {
  readonly directory: string;
  readonly prefix: string;
}

/** One case: its name, kind, inputs and `expected…` values, as the file holds them. */
export type VectorCase = Readonly<Record<string, unknown>> & { readonly name: string; readonly kind: string };

export interface VectorFile {
  readonly schemaVersion: number;
  readonly area: string;
  readonly prefix: string;
  readonly fixtureKeyId: string;
  readonly cases: readonly VectorCase[];
}

/** A key ring as the fixture files hold it. */
export interface FixtureRing {
  readonly activeKeyId: string;
  readonly keys: readonly { readonly keyId: string; readonly materialBase64: string }[];
}

export interface FixtureKeys {
  readonly signing: FixtureRing;
  readonly publicIds: FixtureRing;
}

/** The run's source: both variables set, or neither (the checked-in set). */
export function vectorSource(env: Readonly<Record<string, string | undefined>> = process.env): VectorSource {
  const directory = env[VECTORS_DIR_VARIABLE];
  const prefix = env[VECTOR_PREFIX_VARIABLE];
  if (!directory && !prefix) return { directory: CHECKED_IN_DIRECTORY, prefix: TEST_PROTOCOL_PREFIX };
  if (!directory || !prefix) {
    throw new Error(`Set both ${VECTORS_DIR_VARIABLE} and ${VECTOR_PREFIX_VARIABLE}, or neither.`);
  }
  return { directory, prefix };
}

/** Reads one vector file and checks its header against the source. */
export function loadVectorFile(source: VectorSource, area: VectorArea): VectorFile {
  const path = join(source.directory, area + FILE_SUFFIX);
  const file = JSON.parse(readFileSync(path, UTF8)) as VectorFile;
  if (file.schemaVersion !== SCHEMA_VERSION) throw new Error(`${path}: schemaVersion must be ${SCHEMA_VERSION}.`);
  if (file.area !== area) throw new Error(`${path}: area must be '${area}'.`);
  if (file.prefix !== source.prefix) throw new Error(`${path}: prefix '${file.prefix}', expected '${source.prefix}'.`);
  if (!Array.isArray(file.cases) || file.cases.length === 0) throw new Error(`${path}: no cases.`);
  return file;
}

/** The fixture rings (TEST-ONLY files; a file without the label is refused). */
export function loadFixtureKeys(directory: string = FIXTURE_KEYS_DIRECTORY): FixtureKeys {
  return { signing: readRing(join(directory, 'signing.json')), publicIds: readRing(join(directory, 'public-id.json')) };
}

function readRing(path: string): FixtureRing {
  const document = JSON.parse(readFileSync(path, UTF8)) as { testOnly?: unknown; ring?: FixtureRing };
  if (document.testOnly !== true || document.ring === undefined) throw new Error(`${path}: not a TEST-ONLY fixture ring.`);
  return document.ring;
}

/** The raw bytes of one ring key. */
export function ringKey(ring: FixtureRing, keyId: string): CryptoBytes {
  const key = ring.keys.find((candidate) => candidate.keyId === keyId);
  if (key === undefined) throw new Error(`Fixture key '${keyId}' is not in the ring.`);
  return base64ToBytes(key.materialBase64);
}

/** A case's string input, or a thrown error naming the case and field. */
export function text(vectorCase: VectorCase, field: string): string {
  const value = vectorCase[field];
  if (typeof value !== 'string') throw new Error(`Vector case '${vectorCase.name}': '${field}' is not a string.`);
  return value;
}

/** A case's numeric input. */
export function integer(vectorCase: VectorCase, field: string): number {
  const value = vectorCase[field];
  if (!Number.isInteger(value)) throw new Error(`Vector case '${vectorCase.name}': '${field}' is not an integer.`);
  return value as number;
}

/** A case's boolean input. */
export function flag(vectorCase: VectorCase, field: string): boolean {
  const value = vectorCase[field];
  if (typeof value !== 'boolean') throw new Error(`Vector case '${vectorCase.name}': '${field}' is not a boolean.`);
  return value;
}

/** The `expected…` string fields of a case. */
export function expectedValues(vectorCase: VectorCase): Record<string, string> {
  const expected: Record<string, string> = {};
  for (const [field, value] of Object.entries(vectorCase)) {
    if (field.startsWith(EXPECTED_PREFIX) && typeof value === 'string') expected[field] = value;
  }
  return expected;
}

/** Standard base64 to bytes (the encoding of `…Base64` fields). */
export function base64ToBytes(value: string): CryptoBytes {
  return new Uint8Array(Buffer.from(value, 'base64'));
}

/** Bytes to standard base64. */
export function bytesToBase64(bytes: Uint8Array): string {
  return Buffer.from(bytes).toString('base64');
}
