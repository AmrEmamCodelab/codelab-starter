// The starter's TEST-ONLY fixture keys: generated fresh with node:crypto, written once to packages/vectors/fixture-keys,
// and listed on the Codelab.Security published-material denylist so a production boot refuses every one of them.
import { generateKeyPairSync, randomBytes } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

/** Key size of every symmetric fixture key (the Codelab.Security minimum). */
export const SYMMETRIC_KEY_BYTES = 32;
export const JSON_INDENT = 2;

/** One marker every fixture file carries; the loaders refuse a file without it. */
export const TEST_ONLY_NOTICE =
  'TEST-ONLY starter fixture key. Generated fresh for the golden vectors (tools/vectors/fixture-keys.mjs) and listed '
  + 'on the Codelab.Security published-material denylist: any boot configured with it fails. Never configure it, '
  + 'never copy it into another repository.';

export const FixtureKinds = Object.freeze({
  signing: 'signing-master',
  publicId: 'public-id',
  serviceAssertion: 'service-assertion-es256',
});

/** The fixture files, by kind. The key ids are what the vector files name in fixtureKeyId. */
export const FIXTURE_FILES = Object.freeze([
  { kind: FixtureKinds.signing, file: 'signing.json', keyIds: ['fxsig1', 'fxsig2'] },
  { kind: FixtureKinds.publicId, file: 'public-id.json', keyIds: ['fxpid1', 'fxpid2'] },
  { kind: FixtureKinds.serviceAssertion, file: 'service-assertion.json', keyIds: ['fxs2s1'] },
]);

const EC_CURVE = 'prime256v1';
const PEM_FORMAT = 'pem';
const LF = '\n';
const CRLF = '\r\n';
const PEM_ARMOUR_LINE = /^-----(?:BEGIN|END) [A-Z0-9 ]+-----$/;

function ringDocument(kind, keyIds, random) {
  return {
    testOnly: true,
    notice: TEST_ONLY_NOTICE,
    kind,
    ring: {
      activeKeyId: keyIds[0],
      keys: keyIds.map((keyId) => ({ keyId, materialBase64: random(SYMMETRIC_KEY_BYTES).toString('base64') })),
    },
  };
}

function serviceAssertionDocument(keyId) {
  const { privateKey, publicKey } = generateKeyPairSync('ec', {
    namedCurve: EC_CURVE,
    privateKeyEncoding: { type: 'pkcs8', format: PEM_FORMAT },
    publicKeyEncoding: { type: 'spki', format: PEM_FORMAT },
  });
  return {
    testOnly: true,
    notice: TEST_ONLY_NOTICE,
    kind: FixtureKinds.serviceAssertion,
    keyId,
    privateKeyPem: privateKey,
    publicKeyPem: publicKey,
  };
}

/**
 * Builds a fresh fixture set: { [file]: document }.
 * @param {(size: number) => Buffer} [random] the byte source (tests inject a deterministic one)
 */
export function generateFixtureDocuments(random = randomBytes) {
  const documents = {};
  for (const { kind, file, keyIds } of FIXTURE_FILES) {
    documents[file] = kind === FixtureKinds.serviceAssertion
      ? serviceAssertionDocument(keyIds[0])
      : ringDocument(kind, keyIds, random);
  }
  return documents;
}

/**
 * Writes the fixture set to directory. Refuses to replace existing keys unless force is set: the checked-in vectors
 * are pinned to them, so a new set means regenerating every vector file.
 * @returns {string[]} the written paths
 */
export function writeFixtureDocuments(directory, documents, { force = false } = {}) {
  const existing = Object.keys(documents).filter((file) => existsSync(join(directory, file)));
  if (existing.length > 0 && !force) {
    throw new Error(`fixture keys already exist (${existing.join(', ')}); pass --force to replace them and then regenerate every vector file`);
  }
  mkdirSync(directory, { recursive: true });
  return Object.entries(documents).map(([file, document]) => {
    const path = join(directory, file);
    writeFileSync(path, `${JSON.stringify(document, null, JSON_INDENT)}${LF}`);
    return path;
  });
}

/** Reads and checks every fixture file. Throws on a missing file, a missing TEST-ONLY marker or a wrong kind. */
export function readFixtureDocuments(directory) {
  const documents = {};
  for (const { kind, file } of FIXTURE_FILES) {
    const path = join(directory, file);
    if (!existsSync(path)) throw new Error(`MISSING fixture key file ${path}`);
    const document = JSON.parse(readFileSync(path, 'utf8'));
    if (document.testOnly !== true || document.notice !== TEST_ONLY_NOTICE) {
      throw new Error(`${path}: not labelled TEST-ONLY (testOnly + notice)`);
    }
    if (document.kind !== kind) throw new Error(`${path}: kind must be ${kind}`);
    documents[file] = document;
  }
  return documents;
}

/**
 * Every spelling of a PEM that configuration may hand the guard: as written, without the trailing newline, with
 * CRLF line ends, and the bare base64 body (whose decoded DER the digest also covers).
 */
export function pemSpellings(pem) {
  const lf = pem.replaceAll(CRLF, LF);
  const crlf = lf.replaceAll(LF, CRLF);
  const body = lf.split(LF).filter((line) => line.length > 0 && !PEM_ARMOUR_LINE.test(line)).join('');
  return [...new Set([lf, lf.trimEnd(), crlf, crlf.trimEnd(), body])];
}

/** Every key value of a fixture set that the denylist must refuse (ring materials, PEM spellings). */
export function fixtureKeyValues(documents) {
  const values = [];
  for (const document of Object.values(documents)) {
    if (document.ring) values.push(...document.ring.keys.map((key) => key.materialBase64));
    if (document.privateKeyPem) values.push(...pemSpellings(document.privateKeyPem));
    if (document.publicKeyPem) values.push(...pemSpellings(document.publicKeyPem));
  }
  return values;
}
