// Runs tools/devseed/user-import/New-SampleUserImport.ps1 (Windows PowerShell 5.1) into a temp folder and reads the
// workbook back with node:zlib: the import template header, two valid password-bound rows and one malformed e-mail.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { inflateRawSync } from 'node:zlib';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { runScript, makeTempDir } from '../../../fork/tests/support/powershell.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(HERE, '..', '..', '..', '..');
const SCRIPT = join(REPO_ROOT, 'tools', 'devseed', 'user-import', 'New-SampleUserImport.ps1');
const EXIT_OK = 0;
const EXIT_USAGE = 2;
const LOCAL_HEADER_SIGNATURE = 0x04034b50;
const LOCAL_HEADER_BYTES = 30;
const DEFLATE = 8;
const STORED = 0;
const OFFSETS = Object.freeze({ method: 8, compressedSize: 18, nameLength: 26, extraLength: 28 });
const SHEET_PART = 'xl/worksheets/sheet1.xml';
const REQUIRED_PARTS = Object.freeze(['[Content_Types].xml', '_rels/.rels', 'xl/workbook.xml', 'xl/_rels/workbook.xml.rels', SHEET_PART]);
const HEADER = Object.freeze(['UserName', 'DisplayName', 'LoginName', 'Upn', 'Kind', 'Binding', 'OrganizationCode', 'Email']);

/** Every part of a zip written in one pass (sizes in the local headers, no data descriptors), by name. */
function readParts(buffer) {
  const parts = new Map();
  let offset = 0;
  while (offset + LOCAL_HEADER_BYTES <= buffer.length && buffer.readUInt32LE(offset) === LOCAL_HEADER_SIGNATURE) {
    const method = buffer.readUInt16LE(offset + OFFSETS.method);
    const size = buffer.readUInt32LE(offset + OFFSETS.compressedSize);
    const nameLength = buffer.readUInt16LE(offset + OFFSETS.nameLength);
    const extraLength = buffer.readUInt16LE(offset + OFFSETS.extraLength);
    const name = buffer.toString('utf8', offset + LOCAL_HEADER_BYTES, offset + LOCAL_HEADER_BYTES + nameLength);
    const start = offset + LOCAL_HEADER_BYTES + nameLength + extraLength;
    const data = buffer.subarray(start, start + size);
    assert.ok(method === DEFLATE || method === STORED, `${name} uses a standard compression method`);
    parts.set(name, (method === DEFLATE ? inflateRawSync(data) : data).toString('utf8'));
    offset = start + size;
  }
  return parts;
}

/** The inline-string cell texts of each row, in order. */
function rowsOf(sheetXml) {
  return [...sheetXml.matchAll(/<row [^>]*>(.*?)<\/row>/g)].map((row) => [...row[1].matchAll(/<t>(.*?)<\/t>/g)].map((cell) => cell[1]));
}

test('writes the template header, two valid password-bound rows and one malformed e-mail row', () => {
  const temp = makeTempDir();
  try {
    const target = join(temp.path, 'sample.xlsx');
    const result = runScript(SCRIPT, ['-Path', target, '-Suffix', 'abc1'], { cwd: REPO_ROOT });

    assert.equal(result.code, EXIT_OK, result.output);
    const parts = readParts(readFileSync(target));
    for (const part of REQUIRED_PARTS) assert.ok(parts.has(part), `${part} is in the package`);
    const rows = rowsOf(parts.get(SHEET_PART));
    assert.deepEqual(rows[0], HEADER);
    assert.deepEqual(rows[1], ['sample.one.abc1', 'Sample One', 'internal', 'password', 'sample.one.abc1@example.test']);
    assert.deepEqual(rows[2], ['sample.two.abc1', 'Sample Two', 'internal', 'password', 'sample.two.abc1@example.test']);
    assert.equal(rows[3].at(-1), 'not-an-email-address');
    assert.equal(rows.length, 4);
  } finally {
    temp.cleanup();
  }
});

test('an organization code makes the rows external members of it', () => {
  const temp = makeTempDir();
  try {
    const target = join(temp.path, 'sample.xlsx');
    const result = runScript(SCRIPT, ['-Path', target, '-Suffix', 'x9', '-OrganizationCode', 'SAMPLE-ORG'], { cwd: REPO_ROOT });

    assert.equal(result.code, EXIT_OK, result.output);
    const rows = rowsOf(readParts(readFileSync(target)).get(SHEET_PART));
    assert.deepEqual(rows[1].slice(2, 5), ['external', 'password', 'SAMPLE-ORG']);
  } finally {
    temp.cleanup();
  }
});

test('refuses a malformed suffix or organization code and writes nothing', () => {
  const temp = makeTempDir();
  try {
    const target = join(temp.path, 'sample.xlsx');

    assert.equal(runScript(SCRIPT, ['-Path', target, '-Suffix', 'a b'], { cwd: REPO_ROOT }).code, EXIT_USAGE);
    assert.equal(runScript(SCRIPT, ['-Path', target, '-OrganizationCode', 'bad code'], { cwd: REPO_ROOT }).code, EXIT_USAGE);
    assert.equal(existsSync(target), false);
  } finally {
    temp.cleanup();
  }
});
