import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, it } from 'node:test';
import { newLookup } from '../new-lookup.mjs';
import { runCli } from '../lib/cli.mjs';
import { ExitCode, LOOKUP_MIGRATIONS_DIR, LOOKUP_TYPES_FILE } from '../lib/constants.mjs';
import {
  addMinutes,
  csharpString,
  newestMigrationTimestamp,
  parseCodes,
  parseNames,
  refusalForDisplayName,
  refusalForLookupKey,
  sentenceOf,
  xmlText,
} from '../lib/lookup.mjs';
import { ScaffoldRefusal } from '../lib/csharp-edit.mjs';

const TYPES = [
  'namespace Starter.Domain.Lookups;',
  '',
  'public static class LookupTypes',
  '{',
  '    // scaffold:lookup-types',
  '}',
  '',
].join('\n');
const INITIAL = 'public static class LookupsMigrationIds\n{\n    public const string InitialCreate = "20261106090000_InitialCreate";\n}\n';
const IDS_FILE = 'backend/src/Starter.Domain/Lookups/DocumentKindIds.cs';
const MIGRATION_FILE = `${LOOKUP_MIGRATIONS_DIR}/20261106090100_AddDocumentKindsLookup.cs`;
const ARGS = [
  '--type', 'document-kinds', '--name', 'DocumentKind', '--codes', 'passport,national-card',
  '--name-ar', 'أنواع الوثائق', '--names-ar', 'جواز سفر;بطاقة وطنية',
];

let root;

function write(relative, content) {
  fs.mkdirSync(path.dirname(path.join(root, relative)), { recursive: true });
  fs.writeFileSync(path.join(root, relative), content);
}

function read(relative) {
  return fs.readFileSync(path.join(root, relative), 'utf8');
}

function snapshot() {
  const files = {};
  const walk = (dir) => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else files[path.relative(root, full)] = fs.readFileSync(full, 'utf8');
    }
  };
  walk(root);
  return files;
}

function run(extra = []) {
  return newLookup([...ARGS, ...extra, '--root', root]);
}

function exitOf(argv) {
  return runCli('usage', () => newLookup([...argv, '--root', root]), { out: () => {}, err: () => {} });
}

beforeEach(() => {
  root = fs.mkdtempSync(path.join(os.tmpdir(), 'scaffold-lookup-test-'));
  write(LOOKUP_TYPES_FILE, TYPES);
  write(`${LOOKUP_MIGRATIONS_DIR}/LookupsMigrationModel.cs`, INITIAL);
});

afterEach(() => fs.rmSync(root, { recursive: true, force: true }));

describe('new-lookup', () => {
  it('writes the type constant, the ids constants and the seed migration', () => {
    const lines = run();

    assert.deepEqual(lines.slice(0, 3), [`changed ${LOOKUP_TYPES_FILE}`, `created ${MIGRATION_FILE}`, `created ${IDS_FILE}`]);
    assert.ok(lines.some((line) => line.startsWith('note: no permission or i18n key')));
    const types = read(LOOKUP_TYPES_FILE);
    assert.match(types, /\/\/\/ <summary>The document kinds lookup \(<see cref="DocumentKindIds"\/>\)\.<\/summary>\n    public const string DocumentKinds = "document-kinds";\n\n    \/\/ scaffold:lookup-types/);
    const ids = read(IDS_FILE);
    assert.match(ids, /public static class DocumentKindIds/);
    assert.match(ids, /\/\/\/ <summary>Passport \(code <c>passport<\/c>\)\.<\/summary>\n    public const int Passport = 1;/);
    assert.match(ids, /public const int NationalCard = 2;/);
    const migration = read(MIGRATION_FILE);
    assert.match(migration, /public sealed class AddDocumentKindsLookup : Migration/);
    assert.match(migration, /public const string MigrationId = "20261106090100_AddDocumentKindsLookup";/);
    assert.match(migration, /new\(Ar: "أنواع الوثائق", En: "Document kinds"\)/);
    assert.match(migration, /new\(Id: DocumentKindIds\.Passport, Code: "passport", Names: new\(Ar: "جواز سفر", En: "Passport"\), SortOrder: 10\),/);
    assert.match(migration, /new\(Id: DocumentKindIds\.NationalCard, Code: "national-card", Names: new\(Ar: "بطاقة وطنية", En: "National card"\), SortOrder: 20\),/);
    assert.match(migration, /LookupSeed\.InsertType\(migrationBuilder, LookupTypes\.DocumentKinds, TypeNames, Items\)/);
    assert.match(migration, /LookupSeed\.DeleteType\(migrationBuilder, LookupTypes\.DocumentKinds, Items\)/);
    assert.doesNotMatch(migration, /\{\{|__/);
  });

  it('takes the English names when given', () => {
    run(['--name-en', 'Kinds of "document"', '--names-en', 'Passport book;ID card']);

    const migration = read(MIGRATION_FILE);
    assert.match(migration, /En: "Kinds of \\"document\\""/);
    assert.match(migration, /En: "ID card"\), SortOrder: 20/);
  });

  it('is deterministic: the same arguments on the same tree give the same bytes', () => {
    run();
    const first = snapshot();
    fs.rmSync(root, { recursive: true, force: true });
    fs.mkdirSync(root);
    write(LOOKUP_TYPES_FILE, TYPES);
    write(`${LOOKUP_MIGRATIONS_DIR}/LookupsMigrationModel.cs`, INITIAL);

    run();

    assert.deepEqual(snapshot(), first);
  });

  it('is idempotent: a second run changes nothing, even after the names were edited by hand', () => {
    run();
    write(MIGRATION_FILE, `${read(MIGRATION_FILE)}// reviewed\n`);
    const before = snapshot();

    const lines = run();

    assert.deepEqual(snapshot(), before);
    assert.ok(lines[0].startsWith('unchanged LookupTypes.DocumentKinds, DocumentKindIds and AddDocumentKindsLookup'));
  });

  it('orders the next lookup after the newest migration', () => {
    run();
    newLookup(['--type', 'priorities', '--name', 'Priority', '--codes', 'low,high', '--name-ar', 'الأولويات', '--names-ar', 'منخفضة;عالية', '--root', root]);

    assert.ok(fs.existsSync(path.join(root, LOOKUP_MIGRATIONS_DIR, '20261106090200_AddPrioritiesLookup.cs')));
  });

  for (const [label, argv, message] of [
    ['a non-kebab type', ['--type', 'Document_Kinds', '--name', 'DocumentKind', '--codes', 'a', '--name-ar', 'أ', '--names-ar', 'أ'], /not kebab-case/],
    ['a non-Pascal name', ['--type', 'kinds', '--name', 'documentKind', '--codes', 'a', '--name-ar', 'أ', '--names-ar', 'أ'], /PascalCase/],
    ['a name ending in Ids', ['--type', 'kinds', '--name', 'KindIds', '--codes', 'a', '--name-ar', 'أ', '--names-ar', 'أ'], /must not end with 'Ids'/],
    ['missing codes', ['--type', 'kinds', '--name', 'Kind', '--name-ar', 'أ', '--names-ar', 'أ'], /--codes is required/],
    ['a duplicate code', ['--type', 'kinds', '--name', 'Kind', '--codes', 'a,a', '--name-ar', 'أ', '--names-ar', 'أ;ب'], /listed twice/],
    ['codes that clash as constants', ['--type', 'kinds', '--name', 'Kind', '--codes', 'a1b,a-1b', '--name-ar', 'أ', '--names-ar', 'أ;ب'], /same constant name 'A1b'/],
    ['missing Arabic value names', ['--type', 'kinds', '--name', 'Kind', '--codes', 'a', '--name-ar', 'أ'], /--names-ar is required/],
    ['missing Arabic type name', ['--type', 'kinds', '--name', 'Kind', '--codes', 'a', '--names-ar', 'أ'], /Arabic type name/],
    ['a name count mismatch', ['--type', 'kinds', '--name', 'Kind', '--codes', 'a,b', '--name-ar', 'أ', '--names-ar', 'أ'], /has 1 names for 2 codes/],
    ['a blank value name', ['--type', 'kinds', '--name', 'Kind', '--codes', 'a,b', '--name-ar', 'أ', '--names-ar', 'أ; '], /is required/],
    ['a code that gives the class name', ['--type', 'kinds', '--name', 'Kind', '--codes', 'kind-ids', '--name-ar', 'أ', '--names-ar', 'أ'], /class name 'KindIds'/],
  ]) {
    it(`refuses ${label} and writes nothing`, () => {
      const before = snapshot();

      assert.throws(() => newLookup([...argv, '--root', root]), (error) => error instanceof ScaffoldRefusal && message.test(error.message));
      assert.equal(exitOf(argv), ExitCode.refused);
      assert.deepEqual(snapshot(), before);
    });
  }

  it('refuses a type key already held by another constant', () => {
    write(LOOKUP_TYPES_FILE, TYPES.replace('    // scaffold', '    public const string Kinds = "document-kinds";\n\n    // scaffold'));
    const before = snapshot();

    assert.throws(() => run(), /already declared under another constant name/);
    assert.deepEqual(snapshot(), before);
  });

  it('refuses a constant that holds another key', () => {
    write(LOOKUP_TYPES_FILE, TYPES.replace('    // scaffold', '    public const string DocumentKinds = "other";\n\n    // scaffold'));

    assert.throws(() => run(), /already holds "other"/);
  });

  it('refuses a half-finished earlier run, naming what is there', () => {
    write(IDS_FILE, 'namespace Starter.Domain.Lookups;\n');
    const before = snapshot();

    assert.throws(() => run(), /An earlier run left part of this lookup \(backend\/src\/Starter.Domain\/Lookups\/DocumentKindIds.cs\)/);
    assert.deepEqual(snapshot(), before);
  });

  it('refuses when the Lookups module or its first migration is missing', () => {
    fs.rmSync(path.join(root, LOOKUP_MIGRATIONS_DIR, 'LookupsMigrationModel.cs'));
    assert.throws(() => run(), /holds no migration/);
    fs.rmSync(path.join(root, LOOKUP_MIGRATIONS_DIR), { recursive: true });
    assert.throws(() => run(), /is the Lookups module installed/);
    fs.rmSync(path.join(root, LOOKUP_TYPES_FILE));
    assert.throws(() => run(), /LookupTypes.cs is missing/);
  });

  it('answers usage errors with exit 2 and help with the usage line', () => {
    assert.equal(exitOf(['--name', 'Kind']), ExitCode.usage);
    assert.equal(exitOf(['--type', 'kinds', '--name', 'Kind', '--unknown', 'x']), ExitCode.usage);
    assert.match(newLookup(['--help'])[0], /^Usage: node tools\/scaffold\/new-lookup\.mjs --type <kebab>/);
  });
});

describe('new-lookup helpers', () => {
  it('adds minutes across hour, day, month and year boundaries in UTC', () => {
    assert.equal(addMinutes('20261106090000', 1), '20261106090100');
    assert.equal(addMinutes('20261106235900', 1), '20261107000000');
    assert.equal(addMinutes('20261130235930', 1), '20261201000030');
    assert.equal(addMinutes('20261231235900', 1), '20270101000000');
  });

  it('finds the newest migration timestamp in any file of the folder', () => {
    write(`${LOOKUP_MIGRATIONS_DIR}/X.cs`, 'const string A = "20261201000000_AddX"; const string B = "20261105000000_Old";');
    write(`${LOOKUP_MIGRATIONS_DIR}/notes.txt`, '"20991231000000_Ignored"');

    assert.equal(newestMigrationTimestamp(root), '20261201000000');
  });

  it('checks keys and display names', () => {
    assert.equal(refusalForLookupKey('contact-topics', 'x'), null);
    assert.match(refusalForLookupKey('a'.repeat(65), 'x'), /longer than 64/);
    assert.match(refusalForLookupKey('', 'x'), /required/);
    assert.equal(refusalForDisplayName('اسم', 'x'), null);
    assert.match(refusalForDisplayName('a\nb', 'x'), /one line/);
    assert.match(refusalForDisplayName('a'.repeat(201), 'x'), /longer than 200/);
  });

  it('parses lists and defaults the English names', () => {
    assert.deepEqual(parseCodes(' general , my-account '), ['general', 'my-account']);
    assert.deepEqual(parseNames(undefined, { codes: ['a'], flag: '--names-en', defaults: ['A'] }), ['A']);
    assert.deepEqual(parseNames('One, two;Three', { codes: ['a', 'b'], flag: '--names-en', defaults: null }), ['One, two', 'Three']);
    assert.equal(sentenceOf('my-account'), 'My account');
  });

  it('escapes C# strings and XML doc text', () => {
    assert.equal(csharpString('a "b" \\c'), 'a \\"b\\" \\\\c');
    assert.equal(xmlText('a < b & c > d'), 'a &lt; b &amp; c &gt; d');
  });
});
