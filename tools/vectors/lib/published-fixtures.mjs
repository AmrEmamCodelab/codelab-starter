// packages/dotnet/Codelab.Security/PublishedMaterial/published-material.fixtures.json: the fixture keys on the
// published-material denylist. Same scheme as published-material.source.json (PublishedMaterialDenylist.EntryFor):
// for every key value v and every digest d in digestsOf(v), the entry is sha256(hexDecode(d)). Digests of digests,
// never values, so the file itself holds nothing a scanner or a reader could reuse.
import { readFileSync } from 'node:fs';
import { digestsOf } from '../../extract/lib/key-material.mjs';
import { sha256Hex } from '../../extract/lib/hashing.mjs';
import { fixtureKeyValues, JSON_INDENT } from './fixture-keys.mjs';

export const ENTRIES_PROPERTY = 'keyDigestHashes';
export const PUBLISHED_FIXTURES_PATH = 'packages/dotnet/Codelab.Security/PublishedMaterial/published-material.fixtures.json';
export const FIXTURE_KEYS_PATH = 'packages/vectors/fixture-keys';

const DESCRIPTION =
  'The starter\'s TEST-ONLY fixture keys (packages/vectors/fixture-keys), refused by Codelab.Security key-ring guards. '
  + 'Each entry is sha256(hexDecode(d)) for every digest d of tools/extract/lib/key-material.mjs digestsOf(value): '
  + 'digests of digests, never values. Written by: node tools/vectors/fixture-keys.mjs published-material.';
const HEX_ENCODING = 'hex';
const LF = '\n';

/** The denylist entry of one key digest (hex SHA-256): the SHA-256 of its 32 bytes. */
export function entryFor(keyDigestHex) {
  return sha256Hex(Buffer.from(keyDigestHex, HEX_ENCODING));
}

/** The sorted, distinct entries for a list of key values. */
export function entriesForValues(values) {
  const entries = new Set(values.flatMap((value) => digestsOf(value)).map(entryFor));
  return [...entries].sort();
}

/** The file content for a fixture set. */
export function publishedFixturesText(documents) {
  const document = {
    description: DESCRIPTION,
    source: FIXTURE_KEYS_PATH,
    [ENTRIES_PROPERTY]: entriesForValues(fixtureKeyValues(documents)),
  };
  return `${JSON.stringify(document, null, JSON_INDENT)}${LF}`;
}

/** True when the file at path holds exactly the entries of the fixture set. */
export function isPublishedFixturesFileCurrent(path, documents) {
  try {
    return readFileSync(path, 'utf8') === publishedFixturesText(documents);
  } catch {
    // An unreadable or missing file is simply not current; the caller reports it.
    return false;
  }
}
