#!/usr/bin/env node
// Adds a status and/or a transition to a record type's workflow (module 8) in the module that owns it: the status
// constant (next explicit int) and its status key, the TransitionRule entry (action constant, permission, side,
// reason, label, explicit notification decision), the ar/en wording and label keys, TransitionFixture test stubs in
// the module's test project, and a MODULE.md note. Then it regenerates docs/generated (workflow sheets, statuses.md).
// Idempotent; --dry-run prints the files it would write and writes nothing.
//
// Usage: see USAGE below or --help. Exit codes: 0 done (or already present), 1 refused (nothing changed),
// 2 usage, or the files were written but a downstream generator failed.
import { pathToFileURL } from 'node:url';
import { runCli } from './lib/cli.mjs';
import { DOCS_GENERATED_DIR, DOCSGEN_PROJECT, Report } from './lib/constants.mjs';
import { parseTransitionArguments, transitionRequest } from './lib/transition/arguments.mjs';
import { DryRunReport, TransitionFlags as F, TransitionSwitches as S } from './lib/transition/constants.mjs';
import { planTransition } from './lib/transition/plan.mjs';
import { ChangeSet, regenerate, resolveDotnet } from './lib/workspace.mjs';

export const USAGE = [
  `Usage: node tools/scaffold/new-transition.mjs ${F.module} <Module>`,
  `  [${F.status} <kebab> ${F.statusEn} <text|requester;staff;admin> ${F.statusAr} <text|requester;staff;admin> [${S.terminal}]]`,
  `  [${F.action} <kebab> ${F.from} <status[,status]> ${F.to} <status> ${F.permission} <key> ${F.side} <requester|staff|system>`,
  `   [${F.reason} <none|optional|required>] [${F.reasonField} <field>] [${S.owner}] [${F.labelEn} <text> ${F.labelAr} <text>]`,
  `   ${F.notify} <none|requester[,assignee]> [${F.template} <key>]]`,
  `  [${F.i18nPrefix} <camelCase>] [${S.dryRun}] [${S.noGenerate}] [${S.skipGenerate}] [${F.root} <dir>]`,
].join('\n');

function fixtureCommand(module, env) {
  return `${resolveDotnet(env)} test backend/tests/Modules/Starter.Modules.${module}.Tests --filter FullyQualifiedName~Workflow`;
}

function docsCommand(env) {
  return `${resolveDotnet(env)} run --project ${DOCSGEN_PROJECT} -- --out ${DOCS_GENERATED_DIR}`;
}

function dryRunLines(changes) {
  return [...changes.files.keys()].map((file) => `${changes.exists(file) ? DryRunReport.wouldChange : DryRunReport.wouldCreate} ${file}`);
}

function generatorLines(request, env) {
  if (!request.generate) {
    return request.skipGenerate
      ? [`${Report.skipped} downstream generators: ${S.skipGenerate}`]
      : [`${Report.next} ${docsCommand(env)}`];
  }
  return regenerate(request.root, { apiClient: false, env });
}

/** Runs the generator; returns the report lines. */
export function newTransition(argv, env = process.env) {
  const options = parseTransitionArguments(argv);
  if (options.help) return [USAGE];
  const request = transitionRequest(options);
  const changes = new ChangeSet(request.root);
  const plan = planTransition(changes, request);
  const notes = plan.notes.map((note) => `${Report.note} ${note}`);
  if (request.dryRun) return [...dryRunLines(changes), ...notes, `${Report.note} dry run: nothing was written`];
  const written = changes.commit();
  const lines = written.length === 0 ? [`${Report.unchanged} the workflow of ${request.module} already holds every part of this request`] : written;
  const statusLine = plan.statusValue === null ? [] : [`${Report.note} status ${request.status.key} = ${plan.statusValue}`];
  const reachability = request.status && !request.rule
    ? [`${Report.note} no transition reaches ${request.status.key} yet; the host refuses to start until one does (add it with ${F.action} ... ${F.to} ${request.status.key})`]
    : [];
  return [
    ...lines,
    ...statusLine,
    ...notes,
    ...reachability,
    ...generatorLines(request, env),
    `${Report.next} ${fixtureCommand(request.module, env)}`,
  ];
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.exitCode = runCli(USAGE, () => newTransition(process.argv.slice(2)));
}
