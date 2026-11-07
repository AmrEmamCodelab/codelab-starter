// Command handling for comment-diff.mjs, separate from process wiring so tests can drive it.
import { Dialect, SUPPORTED_EXTENSIONS, dialectForPath, isDialect, scan } from './dialects.mjs';
import { countComments } from './count.mjs';
import { readSource } from './source-file.mjs';
import { VerifyOutcome, verifyCommentsOnly } from './verify.mjs';

export const ExitCode = Object.freeze({
  COMMENTS_ONLY: 0,
  CODE_CHANGED: 1,
  USAGE: 2,
});

export const Command = Object.freeze({
  COUNT: 'count',
  VERIFY: 'verify',
});

const JSON_FLAG = '--json';
const DIALECT_FLAG = '--dialect';
const VERIFY_FILE_COUNT = 2;
const JSON_INDENT = 2;

export const USAGE = [
  'usage: node comment-diff.mjs count <file> [<file> ...] [--json] [--dialect <dialect>]',
  '       node comment-diff.mjs verify <baseline> <edited> [--json] [--dialect <dialect>]',
  `dialects: ${Object.values(Dialect).join(', ')} (default: from the file extension: ${SUPPORTED_EXTENSIONS.join(' ')})`,
  'exit: 0 comments only (verify) or counted (count), 1 code changed, 2 usage, unreadable or unsupported file',
].join('\n');

/**
 * @param {string[]} args the arguments after the script name
 * @param {{ out: (line: string) => void, err: (line: string) => void, read?: (path: string) => string }} io
 * @returns {number} the exit code
 */
export function run(args, io) {
  const read = io.read ?? readSource;
  try {
    const options = parseArguments(args);
    return options.command === Command.COUNT ? runCount(options, io, read) : runVerify(options, io, read);
  } catch (error) {
    io.err(error instanceof UsageError ? `${error.message}\n${USAGE}` : `comment-diff: ${error.message}`);
    return ExitCode.USAGE;
  }
}

class UsageError extends Error {}

export function parseArguments(args) {
  const [command, ...rest] = args;
  if (!Object.values(Command).includes(command)) {
    throw new UsageError(`unknown command '${command ?? ''}'`);
  }
  const options = { command, json: false, dialect: null, files: [] };
  for (let index = 0; index < rest.length; index += 1) {
    if (rest[index] === JSON_FLAG) {
      options.json = true;
    } else if (rest[index] === DIALECT_FLAG) {
      options.dialect = requireDialect(rest[index + 1]);
      index += 1;
    } else {
      options.files.push(rest[index]);
    }
  }
  requireFileCount(options);
  return options;
}

function requireDialect(value) {
  if (!isDialect(value)) {
    throw new UsageError(`unknown dialect '${value ?? ''}'`);
  }
  return value;
}

function requireFileCount(options) {
  if (options.command === Command.VERIFY && options.files.length !== VERIFY_FILE_COUNT) {
    throw new UsageError('verify needs exactly <baseline> and <edited>');
  }
  if (options.files.length === 0) {
    throw new UsageError('count needs at least one file');
  }
}

/** The explicit dialect, else the edited file's, else the baseline's (a baseline copy may have another extension). */
export function resolveDialect(explicit, paths) {
  const dialect = explicit ?? [...paths].reverse().map(dialectForPath).find((value) => value !== null) ?? null;
  if (dialect === null) {
    throw new Error(`unsupported file type: ${paths.join(', ')}. Verify by careful reading instead.`);
  }
  return dialect;
}

function runCount(options, io, read) {
  const results = options.files.map((path) => {
    const dialect = resolveDialect(options.dialect, [path]);
    const text = read(path);
    return { path, dialect, ...countComments(text, scan(text, dialect)) };
  });
  if (options.json) {
    io.out(JSON.stringify(results, null, JSON_INDENT));
  } else {
    results.forEach((result) => io.out(formatCount(result)));
  }
  return ExitCode.COMMENTS_ONLY;
}

function formatCount(result) {
  return [
    result.path,
    `  comment units : ${result.units}   (of which doc comments: ${result.docUnits})`,
    `  comment lines : ${result.commentLines}`,
    `  file lines    : ${result.fileLines}`,
  ].join('\n');
}

function runVerify(options, io, read) {
  const [baselinePath, editedPath] = options.files;
  const dialect = resolveDialect(options.dialect, options.files);
  const result = verifyCommentsOnly({ baselineText: read(baselinePath), editedText: read(editedPath), dialect });
  if (options.json) {
    io.out(JSON.stringify({ baselinePath, editedPath, dialect, ...result }, null, JSON_INDENT));
  } else {
    io.out(formatVerify(result, baselinePath, editedPath));
  }
  return result.outcome === VerifyOutcome.COMMENTS_ONLY ? ExitCode.COMMENTS_ONLY : ExitCode.CODE_CHANGED;
}

function formatVerify(result, baselinePath, editedPath) {
  if (result.outcome === VerifyOutcome.COMMENTS_ONLY) {
    return 'PASS: the code is identical once comments are stripped. Only comments changed.';
  }
  return [
    'FAIL: the code differs once comments are stripped. Something other than a comment changed; revert it.',
    `  baseline ${baselinePath}:${result.baseline.line}  ...${result.baseline.context}...`,
    `  edited   ${editedPath}:${result.edited.line}  ...${result.edited.context}...`,
  ].join('\n');
}
