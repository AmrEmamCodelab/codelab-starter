#!/usr/bin/env node
// Adds a lookup type (module 4, Lookups): the <Name>Ids constants class and the LookupTypes constant in
// backend/src/Starter.Domain/Lookups, and the seed migration (Add<Type>Lookup) in the Lookups module, which inserts
// the type and its values with their Arabic and English names. Lookups are seeded only by migrations. With the code it
// writes the seed test (Starter.Modules.Lookups.Tests/Infrastructure/Seeds), adds the migration to the model test's
// migration list, adds the row of the seeded-types table in the Lookups MODULE.md and keeps the add-lookup recipe in
// the module's rules. No permission or i18n key is needed: lookups.view and lookups.manage cover every type, and the
// names are data; nothing in docs/generated changes. Idempotent; --dry-run prints the files it would write.
//
// Usage: node tools/scaffold/new-lookup.mjs --type <kebab> --name <Pascal> --codes <a,b,...> --name-ar <text>
//        --names-ar <a;b;...> [--name-en <text>] [--names-en <a;b;...>] [--root <dir>] [--dry-run]
// Exit codes: 0 done (or already present), 1 refused (nothing changed), 2 usage.
import { pathToFileURL } from 'node:url';
import { Flags, LOOKUP_MODULE_DIR, LOOKUP_TESTS_PROJECT, ModuleFiles, RULES_COMPILE_SCRIPT, Report } from './lib/constants.mjs';
import { UsageError, parseArguments, runCli } from './lib/cli.mjs';
import { planLookup } from './lib/lookup.mjs';
import { ChangeSet, DRY_RUN_NOTE, resolveDotnet } from './lib/workspace.mjs';

const USAGE = `Usage: node tools/scaffold/new-lookup.mjs ${Flags.type} <kebab> ${Flags.name} <Pascal> ${Flags.codes} <a,b,...> `
  + `${Flags.nameAr} <text> ${Flags.namesAr} <a;b;...> [${Flags.nameEn} <text>] [${Flags.namesEn} <a;b;...>] [${Flags.root} <dir>] [${Flags.dryRun}]`;

const VALUE_FLAGS = [Flags.type, Flags.name, Flags.codes, Flags.nameEn, Flags.nameAr, Flags.namesEn, Flags.namesAr];
const RULES_FILE = `${LOOKUP_MODULE_DIR}/${ModuleFiles.rules}`;

/** What the user runs next: the rules compile when the rules file changed, then the Lookups tests. */
function nextLines(changes, env) {
  const compile = changes.files.has(RULES_FILE) ? [`${Report.next} node ${RULES_COMPILE_SCRIPT}`] : [];
  return [...compile, `${Report.next} ${resolveDotnet(env)} test ${LOOKUP_TESTS_PROJECT}`];
}

/** Runs the generator; returns the report lines. */
export function newLookup(argv, env = process.env) {
  const options = parseArguments(argv, VALUE_FLAGS);
  if (options.help) return [USAGE];
  if (!options.type || !options.name) throw new UsageError(`${Flags.type} and ${Flags.name} are required.`);
  const changes = new ChangeSet(options.root);
  const plan = planLookup(changes, {
    type: options.type,
    name: options.name,
    codes: options.codes,
    nameEn: options['name-en'],
    nameAr: options['name-ar'],
    namesEn: options['names-en'],
    namesAr: options['names-ar'],
  });
  const facts = [
    ...plan.notes.map((note) => `${Report.note} ${note}`),
    `${Report.note} no permission or i18n key is needed (lookups.view / lookups.manage cover every type; the names are data)`,
    `${Report.note} docs/generated does not change (a lookup adds no permission, endpoint, status or configuration key)`,
    `${Report.next} compare ids only through ${plan.idsClass}; read the values with GET /api/lookups/${options.type}`,
  ];
  if (options['dry-run']) return [...changes.preview(), ...facts, `${Report.note} ${DRY_RUN_NOTE}`, ...nextLines(changes, env)];
  const next = nextLines(changes, env);
  const written = changes.commit();
  const lines = plan.unchanged
    ? [`${Report.unchanged} LookupTypes.${plan.typeConst}, ${plan.idsClass} and ${plan.migrationClass} already exist for "${options.type}"`]
    : written;
  return [...lines, ...facts, ...next];
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.exitCode = runCli(USAGE, () => newLookup(process.argv.slice(2)));
}
