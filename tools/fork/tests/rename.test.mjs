// Unit tests of the renameMap engine. Run: node --test "tools/fork/tests/**/*.test.mjs"
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { after, describe, it } from 'node:test';
import { createRenamer, expandRenameMap, isIdentityMap, isLeftBoundary, isRightBoundary, renameWithVariants, withProtectedNames } from '../lib/rename.mjs';
import { carryLeftovers, listTreeFiles, renamedDirectories, renameWorkingTree } from '../lib/rename-tree.mjs';
import { makeTempDir } from './support/powershell.mjs';
import { write } from './support/synthetic-starter.mjs';

const ACME = [{ from: 'Starter', to: 'Acme' }];

describe('expandRenameMap', () => {
  it('adds the lower- and upper-case variants, longest first', () => {
    const variants = expandRenameMap([{ from: 'Starter', to: 'MohPermits' }, { from: 'Starter.Modules', to: 'Moh.Modules' }]);
    assert.deepEqual(variants.map((variant) => variant.from), ['Starter.Modules', 'starter.modules', 'STARTER.MODULES', 'Starter', 'starter', 'STARTER']);
    assert.equal(variants.find((variant) => variant.from === 'STARTER').to, 'MOHPERMITS');
    assert.equal(variants.find((variant) => variant.from === 'starter').to, 'mohpermits');
  });

  it('lets an explicit entry win over a derived case variant', () => {
    const variants = expandRenameMap([{ from: 'codelab-starter', to: 'acme' }, { from: 'CODELAB-STARTER', to: 'OTHER' }]);
    assert.equal(variants.find((variant) => variant.from === 'CODELAB-STARTER').to, 'OTHER');
    assert.equal(variants.filter((variant) => variant.from === 'CODELAB-STARTER').length, 1);
  });
});

describe('identifier boundaries', () => {
  it('accepts word starts: non-word before, camel hump, acronym end', () => {
    assert.equal(isLeftBoundary('x.Starter', 2, 'Starter'), true);
    assert.equal(isLeftBoundary('myStarter', 2, 'Starter'), true);
    assert.equal(isLeftBoundary('IStarter', 1, 'Starter'), true);
    assert.equal(isLeftBoundary('v2Starter', 2, 'Starter'), true);
  });

  it('refuses mid-word starts', () => {
    assert.equal(isLeftBoundary('Restarter', 2, 'starter'), false);
    assert.equal(isLeftBoundary('XSTARTER', 1, 'STARTER'), false);
    assert.equal(isLeftBoundary('ABStarterX', 1, 'BStarter'), false);
  });

  it('accepts word ends: non-word or digit after, upper after a lower end', () => {
    assert.equal(isRightBoundary('Starter.Api', 0, 'Starter'), true);
    assert.equal(isRightBoundary('StarterDev', 0, 'Starter'), true);
    assert.equal(isRightBoundary('Starter2', 0, 'Starter'), true);
    assert.equal(isRightBoundary('STARTER_X', 0, 'STARTER'), true);
  });

  it('refuses mid-word ends', () => {
    assert.equal(isRightBoundary('Starters', 0, 'Starter'), false);
    assert.equal(isRightBoundary('STARTERX', 0, 'STARTER'), false);
  });
});

describe('createRenamer', () => {
  const renamer = createRenamer(ACME);

  it('renames namespaces, paths and identifiers in every case variant', () => {
    assert.equal(renamer.text('namespace Starter.Modules.A;'), 'namespace Acme.Modules.A;');
    assert.equal(renamer.path('backend/src/Starter.Api/Starter.Api.csproj'), 'backend/src/Acme.Api/Acme.Api.csproj');
    assert.equal(renamer.text('frontend/starter-web'), 'frontend/acme-web');
    assert.equal(renamer.text('STARTER_HOST StarterDev IStarterClock starterWeb'), 'ACME_HOST AcmeDev IAcmeClock acmeWeb');
  });

  it('leaves non-boundary occurrences alone', () => {
    assert.equal(renamer.text('Restarter Starters restarting STARTERS'), 'Restarter Starters restarting STARTERS');
  });

  it('never revisits replaced text (no chained renames)', () => {
    const chain = createRenamer([{ from: 'Starter', to: 'Acme' }, { from: 'Acme', to: 'Other' }]);
    assert.equal(chain.text('Starter Acme'), 'Acme Other');
  });

  it('applies the longest mapping first and honours identity protections', () => {
    const protectedMap = withProtectedNames(ACME, ['codelab.starter', 'starterBase']);
    const guarded = createRenamer(protectedMap);
    assert.equal(guarded.text('codelab.starter.json starterBase codelab-starter.slnx'), 'codelab.starter.json starterBase codelab-acme.slnx');
  });

  it('is the identity for an empty or identity-only map', () => {
    for (const map of [[], [{ from: 'Starter', to: 'Starter' }]]) {
      const identity = createRenamer(map);
      assert.equal(identity.identity, true);
      assert.equal(identity.text('Starter starter'), 'Starter starter');
    }
    assert.equal(isIdentityMap(ACME), false);
  });

  it('keeps line endings and non-ASCII text', () => {
    assert.equal(renamer.text('Starter\r\nقالب Starter\r\n'), 'Acme\r\nقالب Acme\r\n');
  });

  it('renames a whole text and each of its lines the same way', () => {
    const text = 'Starter\nxStarter.Api\nRestarter\n STARTER_A';
    const byLine = text.split('\n').map((line) => renamer.text(line)).join('\n');
    assert.equal(renamer.text(text), byLine);
  });
});

describe('withProtectedNames', () => {
  it('adds identity entries only for a real rename, without duplicates', () => {
    assert.deepEqual(withProtectedNames([], ['codelab.starter']), []);
    assert.deepEqual(withProtectedNames([...ACME, { from: 'codelab.starter', to: 'codelab.starter' }], ['codelab.starter']), [...ACME, { from: 'codelab.starter', to: 'codelab.starter' }]);
    assert.deepEqual(withProtectedNames(ACME, ['codelab.starter']), [...ACME, { from: 'codelab.starter', to: 'codelab.starter' }]);
  });

  it('renameWithVariants with no variants returns the text', () => {
    assert.equal(renameWithVariants('Starter', []), 'Starter');
  });
});

describe('working-tree rename', () => {
  const temp = makeTempDir();
  after(() => temp.cleanup());

  function seed(root) {
    write(root, 'backend/src/Starter.Api/Starter.Api.csproj', '<Project><RootNamespace>Starter.Api</RootNamespace></Project>\n');
    write(root, 'backend/src/Starter.Api/bin/Debug/Starter.Api.dll.txt', 'build output\n');
    write(root, 'backend/src/Starter.Api/obj/project.assets.json', '{}\n');
    write(root, 'backend/src/Modules/Notifications/Application/Feed/StarterFeed.cs', 'namespace Starter.Feed;\n');
    write(root, 'frontend/starter-web/node_modules/pkg/index.js', 'starter\n');
    write(root, 'frontend/starter-web/package.json', '{ "name": "starter-web" }\n');
    write(root, 'packages/npm/tools/src/bin/starter-cli.mjs', '// starter\n');
    write(root, 'feed/nuget/Starter.Pkg.nupkg.txt', 'feed\n');
    write(root, 'artifacts/Starter.log', 'artifact\n');
    write(root, '.git/HEAD', 'ref: refs/heads/main\n');
  }

  it('lists files without installed packages, root feed/artifacts, git metadata or .NET build outputs', () => {
    const root = join(temp.path, 'list');
    seed(root);
    assert.deepEqual(listTreeFiles(root), [
      'backend/src/Modules/Notifications/Application/Feed/StarterFeed.cs',
      'backend/src/Starter.Api/Starter.Api.csproj',
      'frontend/starter-web/package.json',
      'packages/npm/tools/src/bin/starter-cli.mjs',
    ]);
  });

  it('pairs renamed folders deepest first and skips depth-changing moves', () => {
    const pairs = renamedDirectories([
      { from: 'a/Starter.Api/x/f.cs', to: 'a/Acme.Api/x/f.cs' },
      { from: 'Starter/g.cs', to: 'Acme/Sub/g.cs' },
    ]);
    assert.deepEqual(pairs, [{ from: 'a/Starter.Api/x', to: 'a/Acme.Api/x' }, { from: 'a/Starter.Api', to: 'a/Acme.Api' }]);
  });

  it('renames files and folders and carries skipped leftovers into the renamed folders', () => {
    const root = join(temp.path, 'tree');
    seed(root);
    const result = renameWorkingTree(root, createRenamer(ACME));
    assert.ok(existsSync(join(root, 'backend/src/Acme.Api/Acme.Api.csproj')));
    assert.equal(readFileSync(join(root, 'backend/src/Acme.Api/Acme.Api.csproj'), 'utf8'), '<Project><RootNamespace>Acme.Api</RootNamespace></Project>\n');
    assert.ok(existsSync(join(root, 'backend/src/Acme.Api/bin/Debug/Starter.Api.dll.txt')), 'bin carried, its content untouched');
    assert.ok(existsSync(join(root, 'frontend/acme-web/node_modules/pkg/index.js')), 'node_modules carried');
    assert.equal(readFileSync(join(root, 'frontend/acme-web/node_modules/pkg/index.js'), 'utf8'), 'starter\n');
    assert.equal(existsSync(join(root, 'backend/src/Starter.Api')), false);
    assert.equal(existsSync(join(root, 'frontend/starter-web')), false);
    assert.ok(existsSync(join(root, 'backend/src/Modules/Notifications/Application/Feed/AcmeFeed.cs')), 'a module Feed folder is renamed');
    assert.ok(existsSync(join(root, 'packages/npm/tools/src/bin/acme-cli.mjs')), 'a tool bin folder is renamed');
    assert.ok(existsSync(join(root, 'feed/nuget/Starter.Pkg.nupkg.txt')), 'the root feed is never renamed');
    assert.ok(existsSync(join(root, 'artifacts/Starter.log')), 'root artifacts are never renamed');
    assert.deepEqual(result.carried.map((entry) => entry.to).sort(), ['backend/src/Acme.Api/bin', 'backend/src/Acme.Api/obj', 'frontend/acme-web/node_modules']);
  });

  it('keeps a leftover whose target already exists, and does nothing for the identity map', () => {
    const root = join(temp.path, 'clash');
    write(root, 'Starter/bin/a.txt', 'old\n');
    write(root, 'Acme/bin/a.txt', 'new\n');
    assert.deepEqual(carryLeftovers(root, [{ from: 'Starter/x.cs', to: 'Acme/x.cs' }]), []);
    assert.equal(readFileSync(join(root, 'Starter/bin/a.txt'), 'utf8'), 'old\n');
    assert.deepEqual(renameWorkingTree(root, createRenamer([])), { contents: [], moves: [], carried: [] });
  });
});
