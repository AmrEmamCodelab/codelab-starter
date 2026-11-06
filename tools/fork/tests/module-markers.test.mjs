// Unit tests of the module marker stripping and the i18n prefix removal, plus remove-module's marker scan on a
// temp tree. Run: node --test "tools/fork/tests/**/*.test.mjs"
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { after, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';
import { hasModuleMarkers, MarkerError, removeI18nPrefix, stripModuleMarkers, toCamelCase } from '../lib/module-markers.mjs';
import { listMarkerCandidates } from '../lib/remove-module.mjs';

const FIXTURES = join(dirname(fileURLToPath(import.meta.url)), 'fixtures', 'module-markers');
const MODULE = 'ServiceRequests';
const PREFIX = 'serviceRequests';
const BOM = '﻿';

const fixture = (name) => readFileSync(join(FIXTURES, name), 'utf8');
const crlf = (text) => text.replace(/\r?\n/g, '\r\n');

describe('toCamelCase', () => {
  it('lower-cases the first letter only', () => {
    assert.equal(toCamelCase('ServiceRequests'), 'serviceRequests');
    assert.equal(toCamelCase('B'), 'b');
    assert.equal(toCamelCase('HTTPGateway'), 'hTTPGateway');
    assert.equal(toCamelCase(''), '');
  });
});

describe('stripModuleMarkers', () => {
  it('removes marked lines and begin/end blocks of the module only (TypeScript fixture)', () => {
    const result = stripModuleMarkers(fixture('app.routes.before.ts'), MODULE, 'app.routes.ts');
    assert.equal(result.text, fixture('app.routes.after.ts'));
    assert.equal(result.removedLines, 8);
  });

  it('removes HTML-comment markers from a project file and keeps CRLF line endings', () => {
    const before = crlf(fixture('host.before.csproj.xml'));
    const result = stripModuleMarkers(before, MODULE, 'Starter.Api.csproj');
    assert.equal(result.text, crlf(fixture('host.after.csproj.xml')));
    assert.ok(!/(^|[^\r])\n/.test(result.text), 'every line break stays CRLF');
  });

  it('leaves text without markers byte for byte', () => {
    const text = 'const a = 1;\r\n// codelab:module Other\nconst b = 2;';
    assert.deepEqual(stripModuleMarkers(text, MODULE), { text, removedLines: 0 });
  });

  it('does not treat a longer module name or a marker outside a comment as the module', () => {
    assert.equal(hasModuleMarkers('x // codelab:module ServiceRequestsArchive', MODULE), false);
    assert.equal(hasModuleMarkers("const s = 'codelab:module ServiceRequests';", MODULE), false);
    assert.equal(hasModuleMarkers('x //codelab:module ServiceRequests', MODULE), true);
    assert.equal(hasModuleMarkers('<!--codelab:module ServiceRequests-->', MODULE), true);
    assert.equal(hasModuleMarkers('// codelab:module-end ServiceRequests', MODULE), true);
  });

  it('refuses an end without a begin', () => {
    assert.throws(() => stripModuleMarkers('a\n// codelab:module-end ServiceRequests\n', MODULE, 'f.ts'),
      (error) => error instanceof MarkerError && /f\.ts:2: codelab:module-end ServiceRequests without a codelab:module-begin/.test(error.message));
  });

  it('refuses a begin without an end', () => {
    assert.throws(() => stripModuleMarkers('a\n\n// codelab:module-begin ServiceRequests\nb\n', MODULE, 'f.ts'),
      (error) => error instanceof MarkerError && /f\.ts:3: .* has no codelab:module-end/.test(error.message));
  });

  it('refuses a nested begin of the same module', () => {
    const text = '// codelab:module-begin ServiceRequests\n// codelab:module-begin ServiceRequests\n// codelab:module-end ServiceRequests\n';
    assert.throws(() => stripModuleMarkers(text, MODULE, 'f.ts'), /f\.ts:2: .* inside the block opened at line 1/);
  });

  it('removes the lines of another module block nested in the module block', () => {
    const text = 'keep\n// codelab:module-begin ServiceRequests\n// codelab:module-begin Other\nx\n// codelab:module-end Other\n// codelab:module-end ServiceRequests\nkeep too\n';
    assert.equal(stripModuleMarkers(text, MODULE).text, 'keep\nkeep too\n');
  });
});

describe('removeI18nPrefix', () => {
  it('removes the prefix object and flat prefixed keys, keeping order, indent and the final line break', () => {
    const result = removeI18nPrefix(fixture('en.before.json'), PREFIX, 'en.json');
    assert.equal(result.text, fixture('en.after.json'));
    assert.deepEqual(result.removedKeys, ['serviceRequests', 'serviceRequests.flat']);
  });

  it('returns the text unchanged when no key matches', () => {
    const text = '{\n    "a": 1\n}';
    assert.deepEqual(removeI18nPrefix(text, PREFIX), { text, removedKeys: [] });
  });

  it('keeps a BOM, CRLF line endings, a four-space indent and a missing final line break', () => {
    const text = `${BOM}{\r\n    "serviceRequests": {\r\n        "a": "x"\r\n    },\r\n    "b": "y"\r\n}`;
    assert.equal(removeI18nPrefix(text, PREFIX).text, `${BOM}{\r\n    "b": "y"\r\n}`);
  });

  it('keeps Arabic text as it is', () => {
    const text = '{\n  "serviceRequests": { "t": "طلب" },\n  "portal": {\n    "title": "البوابة"\n  }\n}\n';
    assert.equal(removeI18nPrefix(text, PREFIX).text, '{\n  "portal": {\n    "title": "البوابة"\n  }\n}\n');
  });

  it('refuses a file that is not a JSON object', () => {
    assert.throws(() => removeI18nPrefix('{ nope', PREFIX, 'ar.json'), (error) => error instanceof MarkerError && /ar\.json is not valid JSON/.test(error.message));
    assert.throws(() => removeI18nPrefix('[1]', PREFIX, 'ar.json'), /ar\.json is not a JSON object/);
  });
});

describe('listMarkerCandidates', () => {
  const root = mkdtempSync(join(tmpdir(), 'codelab-markers-'));
  after(() => rmSync(root, { recursive: true, force: true }));
  const touch = (relative) => {
    mkdirSync(dirname(join(root, relative)), { recursive: true });
    writeFileSync(join(root, relative), '');
  };

  it('lists .ts, .html and .csproj files under the scan roots, outside skipped and module folders', () => {
    for (const file of [
      'frontend/starter-web/src/app/app.routes.ts',
      'frontend/starter-web/src/app/app.html',
      'frontend/starter-web/src/app/features/service-requests/x.ts',
      'frontend/starter-web/src/assets/i18n/en.json',
      'frontend/starter-web/node_modules/pkg/index.ts',
      'frontend/starter-web/src/.angular/cache.ts',
      'backend/src/Starter.Api/Starter.Api.csproj',
      'backend/src/Starter.Api/obj/x.csproj',
      'backend/src/Modules/ServiceRequests/Starter.Modules.ServiceRequests.csproj',
      'tools/other.ts',
    ]) touch(file);
    const skipped = ['backend/src/Modules/ServiceRequests/', 'frontend/starter-web/src/app/features/service-requests/'];
    assert.deepEqual(listMarkerCandidates(root, skipped), [
      'backend/src/Starter.Api/Starter.Api.csproj',
      'frontend/starter-web/src/app/app.html',
      'frontend/starter-web/src/app/app.routes.ts',
    ]);
  });
});
