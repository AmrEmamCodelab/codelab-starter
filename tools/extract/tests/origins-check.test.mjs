import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { checkOriginsDocument, countPromotions, OriginFindingCode, Promotion } from '../lib/origins-check.mjs';

const CLI = join(dirname(fileURLToPath(import.meta.url)), '..', 'check-origins.mjs');
const EXISTING = 'backend/src/Real.cs';
const exists = (path) => path === EXISTING;
const codes = (findings) => findings.map((finding) => finding.code);

test('a rewrite with an existing target is valid', () => {
  const document = { origins: [{ source: 'a.cs', target: EXISTING, promotion: Promotion.Rewritten }] };
  assert.deepEqual(checkOriginsDocument('b.json', document, exists), []);
});

test('an entry without a promotion is a rewrite and needs an existing target', () => {
  assert.deepEqual(checkOriginsDocument('b.json', { origins: [{ source: 'a.cs', target: EXISTING }] }, exists), []);
  assert.deepEqual(codes(checkOriginsDocument('b.json', { origins: [{ source: 'a.cs', target: 'gone.cs' }] }, exists)),
    [OriginFindingCode.MissingTarget]);
});

test('a rewrite whose target is missing or empty is a finding naming the source', () => {
  const document = { origins: [
    { source: 'a.cs', target: 'gone.cs', promotion: Promotion.Rewritten },
    { source: 'b.cs', promotion: Promotion.Rewritten },
  ] };
  const findings = checkOriginsDocument('b.json', document, exists);
  assert.deepEqual(codes(findings), [OriginFindingCode.MissingTarget, OriginFindingCode.MissingTarget]);
  assert.match(findings[0].detail, /^a\.cs: /);
  assert.equal(findings[0].file, 'b.json');
});

test('a staged entry needs a note or a deferred field, not blank text', () => {
  const document = { origins: [
    { source: 'a.cs', target: 'reference/a.cs', promotion: Promotion.Staged, note: 'Not carried.' },
    { source: 'b.cs', target: 'reference/b.cs', promotion: Promotion.Staged, deferred: 'phase 5' },
    { source: 'c.cs', target: 'reference/c.cs', promotion: Promotion.Staged },
    { source: 'd.cs', target: 'reference/d.cs', promotion: Promotion.Staged, note: '   ' },
  ] };
  assert.deepEqual(codes(checkOriginsDocument('b.json', document, exists)),
    [OriginFindingCode.UnexplainedStaged, OriginFindingCode.UnexplainedStaged]);
});

test('an unknown promotion, a missing source and a missing origins array are findings', () => {
  assert.deepEqual(codes(checkOriginsDocument('b.json', { origins: [{ source: 'a.cs', promotion: 'copied' }] }, exists)),
    [OriginFindingCode.UnknownPromotion]);
  assert.deepEqual(codes(checkOriginsDocument('b.json', { origins: [{ target: EXISTING }] }, exists)),
    [OriginFindingCode.MissingSource]);
  assert.deepEqual(codes(checkOriginsDocument('b.json', {}, exists)), [OriginFindingCode.MalformedFile]);
});

test('countPromotions counts the default as a rewrite', () => {
  const counts = countPromotions([
    { origins: [{ source: 'a' }, { source: 'b', promotion: Promotion.Staged }] },
    { origins: [{ source: 'c', promotion: Promotion.Rewritten }] },
  ]);
  assert.deepEqual(counts, { [Promotion.Rewritten]: 2, [Promotion.Staged]: 1 });
});

function runCliIn(root) {
  return spawnSync(process.execPath, [CLI, '--root', root], { encoding: 'utf8' });
}

test('the CLI exits 0 when valid, 1 on findings and 2 without an origins folder', () => {
  const root = mkdtempSync(join(tmpdir(), 'origins-check-'));
  try {
    assert.equal(runCliIn(root).status, 2);
    mkdirSync(join(root, 'provenance', 'origins'), { recursive: true });
    mkdirSync(join(root, 'src'));
    writeFileSync(join(root, 'src', 'Kept.cs'), '');
    const file = join(root, 'provenance', 'origins', 'batch.json');
    writeFileSync(file, JSON.stringify({ origins: [{ source: 'x.cs', target: 'src/Kept.cs', promotion: Promotion.Rewritten }] }));
    const clean = runCliIn(root);
    assert.equal(clean.status, 0, clean.stderr);
    assert.match(clean.stdout, /1 rewritten, 0 staged; 0 finding\(s\)/);
    writeFileSync(file, JSON.stringify({ origins: [{ source: 'x.cs', target: 'reference/x.cs', promotion: Promotion.Staged }] }));
    const dirty = runCliIn(root);
    assert.equal(dirty.status, 1);
    assert.match(dirty.stderr, /\[unexplained-staged\] x\.cs/);
    writeFileSync(file, '{ not json');
    assert.equal(runCliIn(root).status, 1);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
