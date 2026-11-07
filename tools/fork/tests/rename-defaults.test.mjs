// Unit tests of the default renameMap (init Step 1, ADR-003). Run: node --test "tools/fork/tests/**/*.test.mjs"
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { RENAME_PROTECTED_NAMES } from '../lib/constants.mjs';
import { renameMapErrors } from '../lib/manifest.mjs';
import { CONCATENATED_NAMES, defaultRenameMap, KEBAB_NAMES, productNameForms, STARTER_NAMES } from '../lib/rename-defaults.mjs';
import { createRenamer } from '../lib/rename.mjs';

const ACME = Object.freeze({ prefix: 'ACME', nameEn: 'Acme Portal', nameAr: 'بوابة أكمي' });
const MOH = Object.freeze({ prefix: 'MOHPERMITS', nameEn: 'Moh Permits', nameAr: 'تصاريح الوزارة' });

function toOf(map, from) {
  return map.find((entry) => entry.from === from)?.to;
}

describe('productNameForms', () => {
  it('takes word breaks from nameEn when its letters spell the prefix', () => {
    assert.deepEqual(productNameForms('MOHPERMITS', 'Moh Permits'), { pascal: 'MohPermits', lower: 'mohpermits', upper: 'MOHPERMITS', kebab: 'moh-permits' });
    assert.deepEqual(productNameForms('MOH2GO', 'moh-2 go'), { pascal: 'Moh2Go', lower: 'moh2go', upper: 'MOH2GO', kebab: 'moh-2-go' });
  });

  it('treats the prefix as one word otherwise', () => {
    assert.deepEqual(productNameForms('ACME', 'Acme Portal'), { pascal: 'Acme', lower: 'acme', upper: 'ACME', kebab: 'acme' });
    assert.deepEqual(productNameForms('ACME', 'Acme'), { pascal: 'Acme', lower: 'acme', upper: 'ACME', kebab: 'acme' });
  });

  it('always upper-cases Pascal back to the prefix (the protocol prefix variant)', () => {
    for (const [prefix, nameEn] of [['MOHPERMITS', 'Moh Permits'], ['AB1', 'Something Else'], ['MOH2GO', 'moh-2 go']]) {
      assert.equal(productNameForms(prefix, nameEn).pascal.toUpperCase(), prefix);
    }
  });
});

describe('defaultRenameMap', () => {
  it('maps the solution token, the display names and the concatenated rule-id prefixes', () => {
    const map = defaultRenameMap(ACME);
    assert.equal(toOf(map, STARTER_NAMES.pascal), 'Acme');
    assert.equal(toOf(map, STARTER_NAMES.displayEn), ACME.nameEn);
    assert.equal(toOf(map, STARTER_NAMES.displayAr), ACME.nameAr);
    assert.equal(toOf(map, 'starterapi'), 'acmeapi');
    assert.equal(toOf(map, 'starterinfrastructure'), 'acmeinfrastructure');
    assert.equal(CONCATENATED_NAMES.filter((name) => toOf(map, name) === undefined).length, 0);
  });

  it('adds kebab entries only for a multi-word product', () => {
    assert.equal(defaultRenameMap(ACME).some((entry) => KEBAB_NAMES.includes(entry.from)), false);
    const map = defaultRenameMap(MOH);
    assert.equal(toOf(map, 'starter-web'), 'moh-permits-web');
    assert.equal(toOf(map, 'codelab-starter'), 'codelab-moh-permits');
    assert.equal(toOf(map, 'starter-ops-console'), 'moh-permits-ops-console');
  });

  it('protects the manifest names with identity entries', () => {
    const map = defaultRenameMap(ACME);
    for (const name of RENAME_PROTECTED_NAMES) assert.equal(toOf(map, name), name);
  });

  it('is a valid manifest renameMap', () => {
    assert.deepEqual(renameMapErrors(defaultRenameMap(MOH)), []);
  });

  it('refuses an invalid or reserved prefix and missing names', () => {
    assert.throws(() => defaultRenameMap({ ...ACME, prefix: 'acme' }), RangeError);
    assert.throws(() => defaultRenameMap({ ...ACME, prefix: 'STARTER' }), /reserved/);
    assert.throws(() => defaultRenameMap({ ...ACME, nameEn: '  ' }), TypeError);
    assert.throws(() => defaultRenameMap({ ...ACME, nameAr: undefined }), TypeError);
  });
});

describe('defaultRenameMap applied by the rename engine', () => {
  const acme = createRenamer(defaultRenameMap(ACME));
  const moh = createRenamer(defaultRenameMap(MOH));

  it('renames namespaces, project, solution and assembly names', () => {
    assert.equal(acme.text('namespace Starter.Modules.Identity;'), 'namespace Acme.Modules.Identity;');
    assert.equal(moh.path('backend/src/Starter.Api/Starter.Api.csproj'), 'backend/src/MohPermits.Api/MohPermits.Api.csproj');
    assert.equal(moh.path('codelab-starter.slnx'), 'codelab-moh-permits.slnx');
    assert.equal(acme.path('codelab-starter.slnx'), 'codelab-acme.slnx');
    assert.equal(moh.text('<AssemblyName>Starter.Infrastructure</AssemblyName>'), '<AssemblyName>MohPermits.Infrastructure</AssemblyName>');
  });

  it('renames TS paths, the npm app name, DB names, IIS names and the protocol prefix', () => {
    assert.equal(moh.text('"name": "starter-web"'), '"name": "moh-permits-web"');
    assert.equal(acme.text('frontend/starter-web/src'), 'frontend/acme-web/src');
    assert.equal(moh.text('Database=StarterDev;'), 'Database=MohPermitsDev;');
    assert.equal(moh.text("$script:DefaultAppPoolName = 'StarterApi'"), "$script:DefaultAppPoolName = 'MohPermitsApi'");
    assert.equal(moh.text('"Prefix": "STARTER"'), '"Prefix": "MOHPERMITS"');
    assert.equal(moh.text('X-STARTER-Upload-Ticket'), 'X-MOHPERMITS-Upload-Ticket');
  });

  it('renames display names and rule ids, and leaves protected and non-identifier text alone', () => {
    assert.equal(moh.text('"en": "Codelab Starter", "ar": "قالب كودلاب"'), '"en": "Moh Permits", "ar": "تصاريح الوزارة"');
    assert.equal(moh.text('module: StarterApi\n  - id: starterapi.program-size'), 'module: MohPermitsApi\n  - id: mohpermitsapi.program-size');
    assert.equal(moh.text('codelab.starter.json starterBase starterVersion Restarter'), 'codelab.starter.json starterBase starterVersion Restarter');
  });
});
