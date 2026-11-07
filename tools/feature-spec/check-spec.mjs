#!/usr/bin/env node
// Feature-spec validator (build-feature step 2): checks docs/features/<slug>.md against the format in
// docs/features/TEMPLATE.md. With --require-approved it is the human approval gate the builders run before step 3.
//
// Usage: node tools/feature-spec/check-spec.mjs <spec.md> [--require-approved] [--json]
// approvedBy must name a person: an agent name, a word such as agent/bot/pipeline, or the pipeline-proof approver
// outside a proof run (CODELAB_PIPELINE_PROOF_APPROVAL=1, set only by Invoke-PipelineProof.ps1) is an error. These
// name checks are an early warning; the approval gate is the PreToolUse hook rule and CODEOWNERS on docs/features/.
// Exit codes: 0 valid (warnings may be printed), 1 error findings, 2 usage or unreadable file.
// Library: import { checkSpec } from 'tools/feature-spec/check-spec.mjs' (or lib/check-spec.mjs).
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { checkSpec } from './lib/check-spec.mjs';
import {
  AGENT_FILE_EXTENSION,
  AGENTS_FOLDER,
  CliOption,
  ExitCode,
  JSON_INDENT,
  PRE_APPROVAL_ENABLED,
  PRE_APPROVAL_VARIABLE,
  Severity,
  USAGE,
  UTF8,
} from './lib/constants.mjs';

export { checkSpec } from './lib/check-spec.mjs';

const FLAGS = Object.freeze(Object.values(CliOption));
const OPTION_PREFIX = '-';
const REPO_ROOT = fileURLToPath(new URL('../..', import.meta.url));

/** The repository's agent names (.claude/agents/<name>.md); none when the folder is absent. */
export function agentNamesIn(root) {
  const folder = path.join(root, AGENTS_FOLDER);
  if (!fs.existsSync(folder)) return [];
  return fs.readdirSync(folder)
    .filter((file) => file.endsWith(AGENT_FILE_EXTENSION))
    .map((file) => file.slice(0, -AGENT_FILE_EXTENSION.length));
}

/** Parses argv; returns null on bad usage. */
export function parseArguments(argv) {
  const files = argv.filter((argument) => !argument.startsWith(OPTION_PREFIX));
  const options = argv.filter((argument) => argument.startsWith(OPTION_PREFIX));
  if (files.length !== 1 || options.some((option) => !FLAGS.includes(option))) return null;
  return {
    file: files[0],
    requireApproved: options.includes(CliOption.requireApproved),
    json: options.includes(CliOption.json),
  };
}

function describe(file, item) {
  const where = item.line === null ? file : `${file}:${item.line}`;
  return `${item.severity.padEnd(Severity.warning.length)} ${where} [${item.ruleId}] ${item.message}`;
}

function report(file, result, json) {
  if (json) {
    console.log(JSON.stringify({ file, ...result }, null, JSON_INDENT));
    return;
  }
  for (const item of result.findings) {
    const print = item.severity === Severity.error ? console.error : console.warn;
    print(describe(file, item));
  }
  const errors = result.findings.filter((item) => item.severity === Severity.error).length;
  const warnings = result.findings.length - errors;
  console.log(`feature spec ${file}: status ${result.status ?? 'unknown'}, ${result.handEditCount} hand edit(s), ${errors} error(s), ${warnings} warning(s).`);
}

/** Runs the CLI; returns the exit code. The pipeline-proof approver is accepted only when env enables it. */
export function main(argv, env = process.env) {
  const parsed = parseArguments(argv);
  if (!parsed) {
    console.error(USAGE);
    return ExitCode.usage;
  }
  let text;
  try {
    text = fs.readFileSync(parsed.file, UTF8);
  } catch (error) {
    console.error(`Cannot read ${parsed.file}: ${error.message}`);
    return ExitCode.usage;
  }
  const result = checkSpec(text, {
    requireApproved: parsed.requireApproved,
    allowPreApproval: env[PRE_APPROVAL_VARIABLE] === PRE_APPROVAL_ENABLED,
    agentNames: agentNamesIn(REPO_ROOT),
  });
  report(parsed.file, result, parsed.json);
  return result.ok ? ExitCode.ok : ExitCode.findings;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  process.exitCode = main(process.argv.slice(2));
}
