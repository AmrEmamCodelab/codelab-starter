// Finds the files a Bash or PowerShell command line would write: output redirections (incl. heredocs), tee,
// in-place editors, copy/move/remove commands, downloads, git worktree writes, PowerShell cmdlets (and their
// aliases), .NET file APIs, and the writes of nested scripts (bash -c, cmd /c, eval, iex, -EncodedCommand).
// It is a best-effort lexer, not a shell: CI is the authoritative gate (ADR-006). A write whose target cannot be
// resolved statically is reported with target null, and the PreToolUse decision fails closed on it (asks).
import { canonicalName, classifyCommand, isChangeDirectory, WriteKinds } from './shell-commands.mjs';

export { WriteKinds };

const OPERATORS = Object.freeze(['&&', '||', '|', ';', '&', '\n']);
const WHITESPACE = /[ \t\r]/;
const DIGITS_ONLY = /^\d+$/;
const ENV_ASSIGNMENT = /^[A-Za-z_][A-Za-z0-9_]*=/;
const HEREDOC_DELIMITER = /^(-?)\s*(['"]?)([A-Za-z_][\w.-]*)\2/;
const UNRESOLVABLE_TARGET = /[$`*?%]|^~|^\(/;

/** Escapes recognised outside quotes: line continuation and characters a shell would otherwise interpret. */
const ESCAPABLE_AFTER_BACKSLASH = new Set([' ', '"', "'", '\n', '>', '<', '|', ';', '&', '$', '`']);

const EchoCommands = new Set(['echo', 'printf', 'write-output']);

// ---------------------------------------------------------------------------------------------------------------
// Heredocs: bodies are cut out before lexing and attached to the line that opened them.
// ---------------------------------------------------------------------------------------------------------------

function findHeredocOpeners(line) {
  const openers = [];
  let index = line.indexOf('<<');
  while (index >= 0) {
    const isHereString = line[index + 2] === '<';
    const match = isHereString ? null : HEREDOC_DELIMITER.exec(line.slice(index + 2));
    if (match) openers.push({ stripTabs: match[1] === '-', delimiter: match[3] });
    index = line.indexOf('<<', index + (isHereString ? 3 : 2));
  }
  return openers;
}

function readHeredocBody(lines, startIndex, { stripTabs, delimiter }) {
  const body = [];
  let index = startIndex;
  while (index < lines.length) {
    const candidate = stripTabs ? lines[index].replace(/^\t+/, '') : lines[index];
    if (candidate.trimEnd() === delimiter) return { body: body.join('\n'), nextIndex: index + 1 };
    body.push(lines[index]);
    index += 1;
  }
  return { body: body.join('\n'), nextIndex: index };
}

/** Splits a command into logical lines (heredoc bodies removed) and the heredoc bodies each line opened. */
export function extractHeredocs(command) {
  const lines = command.split('\n');
  const logicalLines = [];
  let index = 0;
  while (index < lines.length) {
    const line = lines[index];
    let nextIndex = index + 1;
    const bodies = [];
    for (const opener of findHeredocOpeners(line)) {
      const read = readHeredocBody(lines, nextIndex, opener);
      bodies.push(read.body);
      nextIndex = read.nextIndex;
    }
    logicalLines.push({ text: line, heredocBodies: bodies });
    index = nextIndex;
  }
  return logicalLines;
}

// ---------------------------------------------------------------------------------------------------------------
// Lexer: words (quotes removed), operators and redirections.
// ---------------------------------------------------------------------------------------------------------------

function readRedirect(text, index, pendingWord) {
  let operator = '>';
  let length = 1;
  const next = text[index + 1];
  if (next === '>' || next === '|') { operator += next; length += 1; }
  else if (next === '&') { operator = '>&'; length += 1; }
  const fd = DIGITS_ONLY.test(pendingWord) ? pendingWord : '';
  return { token: { type: 'redirect', operator, fd }, length, consumedPendingWord: fd !== '' };
}

function readOperator(text, index) {
  const two = text.slice(index, index + 2);
  if (two === '&>') {
    const append = text[index + 2] === '>';
    return { token: { type: 'redirect', operator: append ? '&>>' : '&>', fd: '' }, length: append ? 3 : 2 };
  }
  const operator = OPERATORS.find((candidate) => text.startsWith(candidate, index));
  return operator ? { token: { type: 'operator', value: operator }, length: operator.length } : null;
}

function readInputRedirect(text, index) {
  if (text.startsWith('<<<', index)) return { token: { type: 'herestring' }, length: 3 };
  if (text.startsWith('<<', index)) {
    const match = HEREDOC_DELIMITER.exec(text.slice(index + 2));
    return { token: { type: 'heredoc' }, length: 2 + (match ? match[0].length : 0) };
  }
  return { token: { type: 'input' }, length: 1 };
}

/** Tokenizes one logical line. Exported for tests. */
export function tokenize(text) {
  const tokens = [];
  let word = '';
  let hasWord = false;
  let quote = null;
  const flushWord = () => {
    if (hasWord) tokens.push({ type: 'word', value: word });
    word = '';
    hasWord = false;
  };

  let index = 0;
  while (index < text.length) {
    const char = text[index];
    if (quote) {
      if (char === quote) quote = null;
      else if (quote === '"' && char === '\\' && (text[index + 1] === '"' || text[index + 1] === '\\')) {
        word += text[index + 1];
        index += 1;
      } else word += char;
      index += 1;
      continue;
    }
    if (char === '"' || char === "'") { quote = char; hasWord = true; index += 1; continue; }
    if (char === '\\' && ESCAPABLE_AFTER_BACKSLASH.has(text[index + 1])) {
      if (text[index + 1] !== '\n') { word += text[index + 1]; hasWord = true; }
      index += 2;
      continue;
    }
    if (WHITESPACE.test(char)) { flushWord(); index += 1; continue; }
    if (char === '>') {
      const read = readRedirect(text, index, hasWord ? word : '');
      if (read.consumedPendingWord) { word = ''; hasWord = false; } else flushWord();
      tokens.push(read.token);
      index += read.length;
      continue;
    }
    if (char === '<') {
      flushWord();
      const read = readInputRedirect(text, index);
      tokens.push(read.token);
      index += read.length;
      continue;
    }
    const operator = readOperator(text, index);
    if (operator) { flushWord(); tokens.push(operator.token); index += operator.length; continue; }
    word += char;
    hasWord = true;
    index += 1;
  }
  flushWord();
  return tokens;
}

// ---------------------------------------------------------------------------------------------------------------
// Segment analysis: one simple command between operators. Per-command knowledge lives in shell-commands.mjs.
// ---------------------------------------------------------------------------------------------------------------

function splitSegments(tokens) {
  const segments = [[]];
  for (const token of tokens) {
    if (token.type === 'operator') segments.push([]);
    else segments.at(-1).push(token);
  }
  return segments.filter((segment) => segment.length > 0);
}

// The current directory as a target base: the PowerShell and POSIX spellings of "here".
const CURRENT_DIRECTORY_REFERENCE = /^(?:\$\{?PWD\}?|\$\(pwd\)|\$\(Get-Location\)|\$pwd)(?=[\/]|$)/i;

function asTarget(raw) {
  if (!raw) return null;
  const expanded = raw.replace(CURRENT_DIRECTORY_REFERENCE, '.');
  return UNRESOLVABLE_TARGET.test(expanded) ? null : expanded;
}

function isFdDuplication(token, target) {
  return token.operator === '>&' && (DIGITS_ONLY.test(target ?? '') || target === '-');
}

/** Splits a segment into its redirect targets and its plain words (redirect targets removed). */
function separateRedirects(segment) {
  const words = [];
  const redirectTargets = [];
  for (let index = 0; index < segment.length; index += 1) {
    const token = segment[index];
    if (token.type === 'word') { words.push(token.value); continue; }
    if (token.type !== 'redirect' && token.type !== 'input') continue;
    const next = segment[index + 1];
    const target = next?.type === 'word' ? next.value : null;
    if (next?.type === 'word') index += 1;
    if (token.type === 'redirect' && !isFdDuplication(token, target)) redirectTargets.push(target);
  }
  return { words, redirectTargets };
}

// Words that only wrap the real command: env assignments, call operators, ForEach-Object/Where-Object and braces of
// a PowerShell script block (`1 | % { Set-Content x y }`).
const COMMAND_WRAPPERS = new Set(['&', '.', '%', '?', 'foreach-object', 'foreach', 'where-object', 'where', '{', '(']);
const BLOCK_EDGE = /^[{(]+|[})]+$/g;

function commandWordsOf(words) {
  let index = 0;
  while (index < words.length && (ENV_ASSIGNMENT.test(words[index]) || COMMAND_WRAPPERS.has(words[index].toLowerCase()))) index += 1;
  return words.slice(index)
    .map((word) => word.replace(BLOCK_EDGE, ''))
    .filter((word) => word !== '');
}

/** Tracks `cd` within one command line, so `cd node_modules && echo x > ../tools/A.cs` resolves correctly. */
const UNKNOWN_DIRECTORY = null;
const POSIX_SEPARATOR = '/';

function joinDirectory(directory, target) {
  if (target === null) return null;
  if (directory === '') return target;
  if (directory === UNKNOWN_DIRECTORY) return isAbsoluteTarget(target) ? target : null;
  return isAbsoluteTarget(target) ? target : `${directory}${POSIX_SEPARATOR}${target}`;
}

const ABSOLUTE_TARGET = /^(?:[a-zA-Z]:)?[\/]/;

function isAbsoluteTarget(target) {
  return ABSOLUTE_TARGET.test(target);
}

function nextDirectory(directory, args) {
  const destination = asTarget(args.find((arg) => !arg.startsWith('-')) ?? null);
  return joinDirectory(directory, destination);
}

function toWrite({ kind, raw, content }, directory) {
  return { kind, raw, target: joinDirectory(directory, asTarget(raw)), content };
}

function analyzeSegment(segment, heredocBody, directory) {
  const { words, redirectTargets } = separateRedirects(segment);
  const commandWords = commandWordsOf(words);
  const [commandWord = '', ...args] = commandWords;
  const echoed = EchoCommands.has(canonicalName(commandWord)) ? args.join(' ') : null;
  const redirectContent = heredocBody ?? echoed;

  const writes = redirectTargets.map((raw) => toWrite({ kind: WriteKinds.Redirect, raw, content: redirectContent }, directory));
  const classified = classifyCommand(commandWord, args);
  writes.push(...classified.targets.map((write) => toWrite({ ...write, content: heredocBody ?? write.content }, directory)));
  for (const nested of classified.nested) writes.push(...detectShellWrites(nested, { directory }));
  if (classified.opaque) writes.push({ kind: classified.opaque, raw: null, target: null, content: null, opaque: true });
  return writes;
}

// .NET file APIs called from PowerShell: [IO.File]::WriteAllText('p', ...), [IO.File]::Copy(a, 'dest'),
// [IO.StreamWriter]::new('p'), New-Object IO.StreamWriter 'p'.
const DOTNET_FILE_CALL = /\[(?:System\.)?IO\.(?:File|FileInfo|StreamWriter|FileStream|Directory)\]::(\w+)\(([^)]*)\)|New-Object\s+(?:System\.)?IO\.(?:StreamWriter|FileStream)\s*(?:-ArgumentList\s*)?\(?([^)\r\n;|]*)/gi;
const DOTNET_WRITE_METHOD = /^(?:WriteAll\w*|AppendAll\w*|Append\w*|Create\w*|Copy|Move|Replace|Delete|Open\w*|new)$/i;
const DOTNET_DESTINATION_SECOND = /^(?:Copy|Move|Replace)$/i;
const QUOTED_ARGUMENT = /^\s*(['"])(.*)\1\s*$/;
const ARGUMENT_SEPARATOR = ',';

function literalArgument(argument) {
  const match = QUOTED_ARGUMENT.exec(argument ?? '');
  return match ? match[2] : null;
}

function dotnetFileWrites(command, directory) {
  const writes = [];
  for (const match of command.matchAll(DOTNET_FILE_CALL)) {
    const method = match[1] ?? 'new';
    if (!DOTNET_WRITE_METHOD.test(method)) continue;
    const args = (match[2] ?? match[3] ?? '').split(ARGUMENT_SEPARATOR);
    const raw = literalArgument(DOTNET_DESTINATION_SECOND.test(method) ? args[1] : args[0]);
    writes.push(toWrite({ kind: `${WriteKinds.DotnetFileApi} ${method}`, raw, content: null }, directory));
  }
  return writes;
}

/**
 * Every file write the command would perform: [{ kind, raw, target, content, opaque? }].
 * target is the path as written (quotes removed, earlier `cd` applied) or null when it cannot be resolved
 * statically; raw is the path text before resolution. opaque writes come from commands that write files nobody can
 * name (patch, git apply, an inline script calling a write API, xargs rm...). content is the text being written when
 * known (heredoc body, echo arguments, -Value), else null.
 */
export function detectShellWrites(command, { directory = '' } = {}) {
  if (!command) return [];
  const writes = [];
  let current = directory;
  for (const line of extractHeredocs(command)) {
    const heredocBody = line.heredocBodies.length > 0 ? line.heredocBodies.join('\n') : null;
    for (const segment of splitSegments(tokenize(line.text))) {
      const hasHeredoc = segment.some((token) => token.type === 'heredoc');
      writes.push(...analyzeSegment(segment, hasHeredoc ? heredocBody : null, current));
      const commandWords = commandWordsOf(separateRedirects(segment).words);
      if (isChangeDirectory(commandWords[0] ?? '')) current = nextDirectory(current, commandWords.slice(1));
    }
  }
  writes.push(...dotnetFileWrites(command, current));
  return writes;
}
