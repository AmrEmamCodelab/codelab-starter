// Regression tests: content the scanners used to skip (UTF-16 text, zip/OOXML containers, unknown binaries, folders
// skipped by the staging walk, terms hidden in identifiers and file names).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { deflateRawSync } from 'node:zlib';
import { decodeText, listFiles, readScannable, xmlToText } from '../lib/files.mjs';
import { readZipEntries } from '../lib/zip.mjs';
import { loadScrubConfiguration, scrubFile, scrubTree } from '../lib/scrubber.mjs';
import { scanTreeForSecrets } from '../lib/secret-scanner.mjs';
import { UNSCANNABLE_RULE_ID } from '../lib/findings.mjs';
import { EXCEPTIONS_FILE, joined, makeTempDirectory, samples, TERMS_FILE, writeTree } from './helpers.mjs';

const configuration = loadScrubConfiguration({ termsFile: TERMS_FILE, exceptionsFile: EXCEPTIONS_FILE });
const NO_DIGESTS = new Map();
const BRAND_LINE = samples.flagged[0].line;
const IP_LINE = samples.flagged.find((entry) => entry.rule === 'ipv4').line;
const PEM_BEGIN = joined('-----BEGIN', ' RSA PRIVATE', ' KEY-----');
const UTF16LE_BOM = Buffer.from([0xff, 0xfe]);
const UTF16BE_BOM = Buffer.from([0xfe, 0xff]);

function utf16be(text) {
  const bytes = Buffer.from(text, 'utf16le');
  bytes.swap16();
  return bytes;
}

/** A zip archive (deflated entries) built in memory: { name: text }. */
function zipOf(entries) {
  const locals = [];
  const centrals = [];
  let offset = 0;
  for (const [name, text] of Object.entries(entries)) {
    const nameBytes = Buffer.from(name, 'utf8');
    const data = Buffer.from(text, 'utf8');
    const compressed = deflateRawSync(data);
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0); local.writeUInt16LE(8, 8);
    local.writeUInt32LE(compressed.length, 18); local.writeUInt32LE(data.length, 22); local.writeUInt16LE(nameBytes.length, 26);
    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0); central.writeUInt16LE(8, 10);
    central.writeUInt32LE(compressed.length, 20); central.writeUInt32LE(data.length, 24);
    central.writeUInt16LE(nameBytes.length, 28); central.writeUInt32LE(offset, 42);
    locals.push(local, nameBytes, compressed);
    centrals.push(central, nameBytes);
    offset += local.length + nameBytes.length + compressed.length;
  }
  const directory = Buffer.concat(centrals);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(Object.keys(entries).length, 8); end.writeUInt16LE(Object.keys(entries).length, 10);
  end.writeUInt32LE(directory.length, 12); end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, directory, end]);
}

const ruleIdsOf = (findings, path) => findings.filter((finding) => finding.path.startsWith(path)).map((finding) => finding.ruleId);

test('decodeText reads UTF-16 LE/BE with and without a BOM, and still rejects real binaries', () => {
  const text = 'starter notes\n';
  assert.equal(decodeText(Buffer.concat([UTF16LE_BOM, Buffer.from(text, 'utf16le')])), text);
  assert.equal(decodeText(Buffer.concat([UTF16BE_BOM, utf16be(text)])), text);
  assert.equal(decodeText(Buffer.from(text, 'utf16le')), text);
  assert.equal(decodeText(utf16be(text)), text);
  assert.equal(decodeText(Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x00, 0x01, 0x02, 0x00, 0xff])), null);
});

test('regression: UTF-16 files are scrubbed and secret-scanned like UTF-8 files', (testContext) => {
  const root = makeTempDirectory(testContext);
  writeTree(root, {
    'notes-utf16.txt': Buffer.concat([UTF16LE_BOM, Buffer.from(`${BRAND_LINE}\n${IP_LINE}\n`, 'utf16le')]),
    'key-utf16.txt': Buffer.from(`${PEM_BEGIN}\nAAAA\n`, 'utf16le'),
  });
  const scrub = scrubTree(root, configuration).findings;
  assert.ok(ruleIdsOf(scrub, 'notes-utf16.txt').includes('term-brand-product'));
  assert.ok(ruleIdsOf(scrub, 'notes-utf16.txt').includes('ipv4'));
  assert.deepEqual(ruleIdsOf(scanTreeForSecrets(root, NO_DIGESTS).findings, 'key-utf16.txt'), ['private-key']);
});

test('regression: zip containers (docx) are opened and their XML text is scanned, runs joined', (testContext) => {
  const root = makeTempDirectory(testContext);
  const documentXml = `<w:document><w:p><w:r><w:t>${BRAND_LINE.slice(0, 9)}</w:t></w:r><w:r><w:t>${BRAND_LINE.slice(9)}</w:t></w:r></w:p>`
    + `<w:p><w:r><w:t>${IP_LINE}</w:t></w:r></w:p></w:document>`;
  writeTree(root, {
    'design-notes.docx': zipOf({ '[Content_Types].xml': '<Types/>', 'word/document.xml': documentXml }),
    'bundle.zip': zipOf({ 'inner/key.txt': `${PEM_BEGIN}\n` }),
  });
  const scrub = scrubTree(root, configuration).findings;
  assert.ok(ruleIdsOf(scrub, 'design-notes.docx!word/document.xml').includes('term-brand-product'));
  assert.ok(ruleIdsOf(scrub, 'design-notes.docx!word/document.xml').includes('ipv4'));
  assert.deepEqual(ruleIdsOf(scanTreeForSecrets(root, NO_DIGESTS).findings, 'bundle.zip!inner/key.txt'), ['private-key']);
});

test('readZipEntries and xmlToText decode entries and entities', () => {
  const [entry] = readZipEntries(zipOf({ 'a.xml': '<p>a &amp; b</p>' }));
  assert.equal(entry.name, 'a.xml');
  assert.equal(xmlToText(entry.data.toString('utf8')), 'a & b\n');
});

test('regression: a binary that is neither text nor a container fails both scanners; images and fonts do not', (testContext) => {
  const root = makeTempDirectory(testContext);
  const opaque = Buffer.from([0x00, 0x01, 0x02, 0x03, 0xff, 0x00, 0x10, 0x00, 0x00, 0x7f]);
  writeTree(root, { 'dump.bin': opaque, 'logo.png': opaque, 'font.woff2': opaque });
  assert.deepEqual(scrubTree(root, configuration).findings.map((finding) => [finding.path, finding.ruleId]), [['dump.bin', UNSCANNABLE_RULE_ID]]);
  assert.deepEqual(scanTreeForSecrets(root, NO_DIGESTS).findings.map((finding) => finding.path), ['dump.bin']);
  assert.equal(readScannable(root, 'logo.png')[0].unscannable, undefined);
});

test('regression: outside git the staging walk scans nested artifacts/, dist/, bin/ and node_modules/', (testContext) => {
  const root = makeTempDirectory(testContext);
  writeTree(root, {
    'artifacts/leak.md': `${BRAND_LINE}\n`,
    'src/dist/leak.js': `${BRAND_LINE}\n`,
    'src/bin/leak.txt': `${BRAND_LINE}\n`,
    'node_modules/x/leak.js': `${BRAND_LINE}\n`,
  });
  assert.deepEqual(listFiles(root), ['artifacts/leak.md', 'node_modules/x/leak.js', 'src/bin/leak.txt', 'src/dist/leak.js']);
  assert.equal(scrubTree(root, configuration).findings.length, 4);
});

test('regression: term rules find terms inside identifiers and file names', () => {
  for (const { rule, path } of samples.pathFlagged) {
    assert.ok(scrubFile(path, '', configuration).map((finding) => finding.ruleId).includes(rule), path);
  }
});
