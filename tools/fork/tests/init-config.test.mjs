// Unit tests of the init config (tools/fork/lib/init-config.mjs): the -ConfigFile shape, its validation and
// normalization, and the module and secret lists derived from it.
// Run: node --test tools/fork/tests/init-config.test.mjs
import assert from 'node:assert/strict';
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { after, describe, it } from 'node:test';
import {
  checkInitConfig,
  INIT_CONFIG_FIELDS,
  MAX_LOGO_BYTES,
  MAX_NAME_LENGTH,
  normalizeInitConfig,
  readInitConfigFile,
  refusalForLogo,
  skippedSecretServices,
  untickedModules,
  validateInitConfig,
} from '../lib/init-config.mjs';
import { makeTempDir } from './support/powershell.mjs';

const FIXTURES = join(dirname(fileURLToPath(import.meta.url)), 'fixtures', 'init-config');
const fixture = (name) => join(FIXTURES, name);
const ALL_KEPT = Object.freeze({ attachments: true, portal: true, sms: true, ops: true });

const VALID = () => ({
  prefix: 'ACME',
  nameEn: 'Acme Portal',
  nameAr: 'بوابة أكمي',
  primaryColor: '#0a5c8f',
  logoPath: null,
  loginModes: ['basic'],
  optionalModules: { ...ALL_KEPT },
  database: { connectionString: null, skip: true },
});

/** Each case breaks one rule. */
const INVALID = Object.freeze({
  'missing field': (config) => { delete config.database; },
  'unknown field': (config) => { config.theme = 'dark'; },
  'lower-case prefix': (config) => { config.prefix = 'acme'; },
  'one-letter prefix': (config) => { config.prefix = 'A'; },
  'blank English name': (config) => { config.nameEn = '   '; },
  'long Arabic name': (config) => { config.nameAr = 'ب'.repeat(MAX_NAME_LENGTH + 1); },
  'short colour': (config) => { config.primaryColor = '#abc'; },
  'colour without #': (config) => { config.primaryColor = '0a5c8f'; },
  'empty logo path': (config) => { config.logoPath = ''; },
  'no login mode': (config) => { config.loginModes = []; },
  'unknown login mode': (config) => { config.loginModes = ['basic', 'kerberos']; },
  'duplicate login mode': (config) => { config.loginModes = ['sso', 'sso']; },
  'login modes not a list': (config) => { config.loginModes = 'basic'; },
  'required module listed': (config) => { config.optionalModules = { ...ALL_KEPT, identity: false }; },
  'optional module missing': (config) => { delete config.optionalModules.ops; },
  'optional module not boolean': (config) => { config.optionalModules.sms = 'no'; },
  'blank connection string': (config) => { config.database.connectionString = ' '; },
  'skip not boolean': (config) => { config.database.skip = 'yes'; },
  'database extra key': (config) => { config.database.server = 'x'; },
});

describe('validateInitConfig', () => {
  it('accepts a valid config', () => {
    assert.deepEqual(validateInitConfig(VALID()), []);
  });

  it('leaves the reserved prefixes to the rename step (a renamed fork reserves its own prefix)', () => {
    assert.deepEqual(validateInitConfig({ ...VALID(), prefix: 'STARTER' }), []);
  });

  it('accepts a connection string with skip false', () => {
    const config = VALID();
    config.database = { connectionString: 'Server=(localdb)\\P2Proof;Database=AcmeDev', skip: false };
    assert.deepEqual(validateInitConfig(config), []);
  });

  for (const [name, breakIt] of Object.entries(INVALID)) {
    it(`rejects: ${name}`, () => {
      const config = VALID();
      breakIt(config);
      assert.notDeepEqual(validateInitConfig(config), [], name);
    });
  }

  it('rejects a non-object and stops at missing fields', () => {
    assert.notDeepEqual(validateInitConfig([]), []);
    assert.notDeepEqual(validateInitConfig(null), []);
    assert.deepEqual(validateInitConfig({}), [`Missing field(s): ${INIT_CONFIG_FIELDS.join(', ')}.`]);
  });
});

describe('normalizeInitConfig', () => {
  it('trims names, lower-cases the colour, orders login modes and modules, resolves the logo', () => {
    const raw = { ...VALID(), nameEn: '  Acme  ', primaryColor: '#0A5C8F', loginModes: ['sso', 'basic'], logoPath: 'logo.svg',
      optionalModules: { ops: false, sms: true, portal: true, attachments: true } };
    const config = normalizeInitConfig(raw, FIXTURES);
    assert.equal(config.nameEn, 'Acme');
    assert.equal(config.primaryColor, '#0a5c8f');
    assert.deepEqual(config.loginModes, ['basic', 'sso']);
    assert.deepEqual(Object.keys(config.optionalModules), ['attachments', 'portal', 'sms', 'ops']);
    assert.equal(config.logoPath, fixture('logo.svg'));
  });
});

describe('refusalForLogo', () => {
  const temp = makeTempDir();
  after(() => temp.cleanup());

  it('accepts no logo and a small image', () => {
    assert.equal(refusalForLogo(null), null);
    assert.equal(refusalForLogo(fixture('logo.svg')), null);
  });

  it('refuses a missing file, a folder, a wrong extension and a file over the limit', () => {
    assert.match(refusalForLogo(join(temp.path, 'none.svg')), /is not a file/);
    assert.match(refusalForLogo(temp.path), /is not a file/);
    const text = join(temp.path, 'logo.txt');
    writeFileSync(text, 'x');
    assert.match(refusalForLogo(text), /must be one of/);
    const big = join(temp.path, 'big.png');
    writeFileSync(big, Buffer.alloc(MAX_LOGO_BYTES + 1));
    assert.match(refusalForLogo(big), /larger than/);
  });
});

describe('readInitConfigFile and checkInitConfig', () => {
  it('reads the valid fixture, logo resolved against the file folder', () => {
    const { config, errors } = readInitConfigFile(fixture('valid.json'));
    assert.equal(errors, undefined);
    assert.equal(config.nameEn, 'Acme Portal');
    assert.equal(config.logoPath, fixture('logo.svg'));
    assert.deepEqual(config.loginModes, ['basic', 'sso']);
  });

  it('reports every field problem of the invalid fixture', () => {
    const { errors } = readInitConfigFile(fixture('invalid-fields.json'));
    const text = errors.join('\n');
    for (const expected of [/Unknown field\(s\): extra/]) assert.match(text, expected);
    const withoutExtra = JSON.parse(readFileSync(fixture('invalid-fields.json'), 'utf8'));
    delete withoutExtra.extra;
    const all = checkInitConfig(withoutExtra, FIXTURES).errors.join('\n');
    for (const expected of [/prefix/i, /nameEn/, /primaryColor/, /loginModes/, /optionalModules/, /database/]) assert.match(all, expected);
  });

  it('refuses a missing file and malformed JSON', () => {
    assert.match(readInitConfigFile(fixture('none.json')).errors[0], /not found/);
    assert.match(readInitConfigFile(fixture('not-json.json')).errors[0], /not valid JSON/);
  });

  it('refuses a logo that does not exist', () => {
    const { errors } = checkInitConfig({ ...VALID(), logoPath: 'missing.png' }, FIXTURES);
    assert.match(errors[0], /is not a file/);
  });
});

describe('derived lists', () => {
  it('lists the unticked optional modules and the secret services to skip, in id order', () => {
    const config = { ...VALID(), optionalModules: { attachments: false, portal: false, sms: true, ops: false } };
    assert.deepEqual(untickedModules(config), ['attachments', 'portal', 'ops']);
    assert.deepEqual(skippedSecretServices(config), ['attachments', 'ops']);
    assert.deepEqual(untickedModules(VALID()), []);
    assert.deepEqual(skippedSecretServices(VALID()), []);
  });
});
