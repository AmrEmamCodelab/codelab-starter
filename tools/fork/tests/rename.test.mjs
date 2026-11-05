// Unit tests of the renameMap engine. Run: node --test "tools/fork/tests/**/*.test.mjs"
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { createRenamer, expandRenameMap, isIdentityMap, isLeftBoundary, isRightBoundary, renameWithVariants, withProtectedNames } from '../lib/rename.mjs';

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
