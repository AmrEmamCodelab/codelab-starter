#!/usr/bin/env node
// Adds a lookup type (module 4, Lookups): the <Name>Ids constants class and the LookupTypes constant in
// backend/src/Starter.Domain/Lookups, and the seed migration (Add<Type>Lookup) in the Lookups module, which inserts
// the type and its values with their Arabic and English names. Lookups are seeded only by migrations. No permission or
// i18n key is needed: lookups.view and lookups.manage cover every type, and the names are data. Idempotent.
//
// Usage: node tools/scaffold/new-lookup.mjs --type <kebab> --name <Pascal> --codes <a,b,...> --name-ar <text>
//        --names-ar <a;b;...> [--name-en <text>] [--names-en <a;b;...>] [--root <dir>]
// Exit codes: 0 done (or already present), 1 refused (nothing changed), 2 usage.
import { pathToFileURL } from 'node:url';
import { Flags, Report } from './lib/constants.mjs';
import { UsageError, parseArguments, runCli } from './lib/cli.mjs';
import { planLookup } from './lib/lookup.mjs';
import { ChangeSet } from './lib/workspace.mjs';

const USAGE = `Usage: node tools/scaffold/new-lookup.mjs ${Flags.type} <kebab> ${Flags.name} <Pascal> ${Flags.codes} <a,b,...> `
  + `${Flags.nameAr} <text> ${Flags.namesAr} <a;b;...> [${Flags.nameEn} <text>] [${Flags.namesEn} <a;b;...>] [${Flags.root} <dir>]`;

const VALUE_FLAGS = [Flags.type, Flags.name, Flags.codes, Flags.nameEn, Flags.nameAr, Flags.namesEn, Flags.namesAr];

/** Runs the generator; returns the report lines. */
export function newLookup(argv) {
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
  const written = changes.commit();
  const lines = plan.unchanged
    ? [`${Report.unchanged} LookupTypes.${plan.typeConst}, ${plan.idsClass} and ${plan.migrationClass} already exist for "${options.type}"`]
    : written;
  return [
    ...lines,
    `${Report.note} no permission or i18n key is needed (lookups.view / lookups.manage cover every type; the names are data)`,
    `${Report.next} compare ids only through ${plan.idsClass}; read the values with GET /api/lookups/${options.type}`,
  ];
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.exitCode = runCli(USAGE, () => newLookup(process.argv.slice(2)));
}
