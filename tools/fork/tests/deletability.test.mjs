// Unit tests of the deletability probe (add-probe) and the remnant check (verify-removed), including a round trip
// probe -> remove-module -> no remnants on a synthetic starter. Run: node --test "tools/fork/tests/**/*.test.mjs"
import assert from 'node:assert/strict';
import { existsSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { after, before, describe, it } from 'node:test';
import { main } from '../fork.mjs';
import { addProbe, addProbeHostBlock, addProbeRoute, addProbeTranslation, findRemnants, probeNames, probeRoutesFile } from '../lib/deletability.mjs';
import { stripModuleMarkers } from '../lib/module-markers.mjs';
import { RefusalError } from '../lib/repo.mjs';
import { makeTempDir } from './support/powershell.mjs';
import { createSyntheticStarter, read, write } from './support/synthetic-starter.mjs';

const EXIT = Object.freeze({ ok: 0, failed: 1, usage: 2 });
const ROUTES = "import type { Routes } from '@angular/router';\n\nexport const routes: Routes = [\n  { path: 'a' },\n];\n";
const HOST = '<Project Sdk="Microsoft.NET.Sdk.Web">\r\n  <ItemGroup />\r\n</Project>\r\n';

function captureIo() {
  const lines = { log: [], error: [] };
  return { io: { log: (line) => lines.log.push(line), error: (line) => lines.error.push(line) }, lines };
}

describe('probe text edits', () => {
  it('derives the probe names', () => {
    assert.deepEqual(probeNames('ServiceRequests'), {
      kebab: 'service-requests',
      camel: 'serviceRequests',
      routesConst: 'SERVICE_REQUESTS_PROBE_ROUTES',
      routesFile: 'frontend/starter-web/src/app/features/service-requests/service-requests.routes.ts',
    });
  });

  it('registers the route behind markers that stripModuleMarkers removes again', () => {
    const probed = addProbeRoute(ROUTES, 'DeletionProbe');
    assert.match(probed, /^import \{ DELETION_PROBE_PROBE_ROUTES \} from '\.\/features\/deletion-probe\/deletion-probe\.routes'; \/\/ codelab:module DeletionProbe\n/);
    assert.match(probed, /export const routes: Routes = \[\n {2}\/\/ codelab:module-begin DeletionProbe\n {2}\.\.\.DELETION_PROBE_PROBE_ROUTES,\n {2}\/\/ codelab:module-end DeletionProbe\n {2}\{ path: 'a' \},/);
    assert.equal(stripModuleMarkers(probed, 'DeletionProbe').text, ROUTES);
  });

  it('keeps CRLF in the routes file and refuses one without the anchor', () => {
    const crlf = ROUTES.replace(/\n/g, '\r\n');
    assert.equal(stripModuleMarkers(addProbeRoute(crlf, 'X'), 'X').text, crlf);
    assert.throws(() => addProbeRoute('export const other = [];\n', 'X'), (error) => error instanceof RefusalError && /has no line 'export const routes: Routes = \['/.test(error.message));
  });

  it('adds a marked host-project block that strips back to the original', () => {
    const probed = addProbeHostBlock(HOST, 'X');
    assert.match(probed, /<!-- codelab:module-begin X -->\r\n {2}<ItemGroup Label="X deletability probe" \/>\r\n {2}<!-- codelab:module-end X -->\r\n<\/Project>/);
    assert.equal(stripModuleMarkers(probed, 'X').text, HOST);
    assert.throws(() => addProbeHostBlock('<Nope />', 'X'), RefusalError);
  });

  it('adds the translation key last, in the two-space format', () => {
    assert.equal(addProbeTranslation('{\n  "a": 1\n}\n', 'ServiceRequests', 'v'), '{\n  "a": 1,\n  "serviceRequests": {\n    "probe": "v"\n  }\n}\n');
  });

  it('writes an empty routes list named after the module', () => {
    assert.match(probeRoutesFile('X'), /export const X_PROBE_ROUTES: Routes = \[\];\n$/);
  });
});

describe('add-probe and verify-removed on a synthetic starter', () => {
  const temp = makeTempDir();
  const root = join(temp.path, 'copy');
  before(() => {
    createSyntheticStarter(root);
    rmSync(join(root, '.git'), { recursive: true, force: true });
  });
  after(() => temp.cleanup());

  it('refuses a root with git metadata', () => {
    write(join(temp.path, 'clone'), '.git', 'gitdir: elsewhere\n');
    assert.throws(() => addProbe(join(temp.path, 'clone'), 'B', captureIo().io), /has \.git; the probe runs only in a temp copy/);
  });

  it('refuses a module that was not scaffolded, and a feature folder that already exists', () => {
    assert.throws(() => addProbe(root, 'Nowhere', captureIo().io), /backend\/src\/Modules\/Nowhere\/ not found; scaffold the module first/);
    assert.throws(() => addProbe(root, 'B', captureIo().io), /features\/b\/ already exists/);
  });

  it('reports every remnant of a present module', () => {
    const remnants = findRemnants(root, 'B');
    for (const expected of [
      /folder backend\/src\/Modules\/B\/ still exists/,
      /codelab-starter\.slnx still lists backend\/src\/Modules\/B\/Starter\.Modules\.B\.csproj/,
      /Starter\.Api\.csproj still references \.\.\/Modules\/B\/Starter\.Modules\.B\.csproj/,
      /app\.routes\.ts still has codelab:module B markers/,
      /Starter\.Api\.csproj still has codelab:module B markers/,
      /en\.json still has key\(s\) b/,
      /removedModules does not record B/,
    ]) assert.ok(remnants.some((line) => expected.test(line)), `${expected} in:\n${remnants.join('\n')}`);
  });

  it('probes module A, removes it with remove-module, and verify-removed then finds nothing', () => {
    rmSync(join(root, 'frontend/starter-web/src/app/features/a'), { recursive: true, force: true });
    write(root, 'frontend/starter-web/src/app/app.routes.ts', ROUTES);
    const probe = captureIo();
    assert.equal(main(['add-probe', '--root', root, '--name', 'A'], probe.io), EXIT.ok, probe.lines.error.join('\n'));
    assert.ok(existsSync(join(root, 'frontend/starter-web/src/app/features/a/a.routes.ts')));
    assert.match(read(root, 'frontend/starter-web/src/assets/i18n/ar.json'), /"probe": "مسبار قابلية الحذف"/);

    const removal = captureIo();
    assert.equal(main(['remove-module', '--root', root, '--name', 'A'], removal.io), EXIT.ok, removal.lines.error.join('\n'));
    assert.equal(read(root, 'frontend/starter-web/src/app/app.routes.ts'), ROUTES);

    const verify = captureIo();
    assert.equal(main(['verify-removed', '--root', root, '--name', 'A'], verify.io), EXIT.ok, verify.lines.error.join('\n'));
    assert.match(verify.lines.log.join('\n'), /Module A is fully removed/);
    const failed = captureIo();
    assert.equal(main(['verify-removed', '--root', root, '--name', 'B'], failed.io), EXIT.failed);
    assert.match(failed.lines.error.join('\n'), /Module B is not fully removed/);
  });

  it('treats a bad or missing name as a usage error', () => {
    for (const argv of [['verify-removed', '--root', root], ['add-probe', '--root', root, '--name', 'bad-name']]) {
      assert.equal(main(argv, captureIo().io), EXIT.usage, argv.join(' '));
    }
  });
});
