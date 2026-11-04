#!/usr/bin/env node
// Rules engine CLI.
//   node tools/rules-engine/compile.mjs            validate all rules, then (re)write every generated file
//   node tools/rules-engine/compile.mjs --check    validate and fail (exit 1) when any generated file drifted
//   --root <dir>                                   repo root (default: two folders above this script)
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { EXIT_CODES, REGENERATE_COMMAND } from './lib/constants.mjs';
import { RulesError } from './lib/errors.mjs';
import { compileRepository, findDrift, hasRulesSources, writeOutputs } from './lib/engine.mjs';

const DEFAULT_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const FLAGS = Object.freeze({ check: '--check', root: '--root', help: '--help' });
const USAGE = `Usage: node tools/rules-engine/compile.mjs [${FLAGS.check}] [${FLAGS.root} <dir>]`;

class UsageError extends Error {}

/** Parses argv into { check, root }; throws UsageError for anything unknown. */
export function parseArguments(argv) {
  const options = { check: false, root: DEFAULT_ROOT, help: false };
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument === FLAGS.check) {
      options.check = true;
    } else if (argument === FLAGS.help) {
      options.help = true;
    } else if (argument === FLAGS.root && argv[index + 1]) {
      options.root = path.resolve(argv[index + 1]);
      index += 1;
    } else {
      throw new UsageError(`Unknown or incomplete argument '${argument}'.`);
    }
  }
  return options;
}

function runCheck(root, outputs) {
  const drift = findDrift(root, outputs);
  if (drift.length === 0) {
    console.log(`rules check: OK (${outputs.length} generated file(s) up to date).`);
    return EXIT_CODES.ok;
  }
  console.error(`rules check: ${drift.length} generated file(s) drifted from the rules sources:`);
  for (const item of drift) {
    console.error(`  - ${item.path}: ${item.reason}`);
  }
  console.error(`Run '${REGENERATE_COMMAND}' and commit the result (delete orphaned files).`);
  return EXIT_CODES.failed;
}

function runWrite(root, outputs) {
  const written = writeOutputs(root, outputs);
  console.log(`rules compile: ${written.length} file(s) written, ${outputs.length - written.length} unchanged.`);
  for (const file of written) {
    console.log(`  wrote ${file}`);
  }
  return EXIT_CODES.ok;
}

export function main(argv) {
  let options;
  try {
    options = parseArguments(argv);
  } catch (error) {
    console.error(`${error.message}\n${USAGE}`);
    return EXIT_CODES.usage;
  }
  if (options.help) {
    console.log(USAGE);
    return EXIT_CODES.ok;
  }
  if (!hasRulesSources(options.root)) {
    console.error(`No rules/rules.schema.json + rules/base.rules.yaml under ${options.root}.`);
    return EXIT_CODES.failed;
  }
  try {
    const { outputs } = compileRepository(options.root);
    return options.check ? runCheck(options.root, outputs) : runWrite(options.root, outputs);
  } catch (error) {
    if (error instanceof RulesError) {
      console.error(error.message);
      return EXIT_CODES.failed;
    }
    throw error;
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(2));
}
