// Recognises the commands docs tell people to run (npm scripts, node scripts, PowerShell scripts, cd) in a line of
// shell text. Shell-agnostic on purpose: the same line may be PowerShell, cmd or bash.

export const CommandKinds = Object.freeze({
  changeDirectory: 'cd',
  npmScript: 'npm-script',
  nodeScript: 'node-script',
  nodeTest: 'node-test',
  powershellScript: 'powershell-script',
});

const SEGMENT_OPERATORS = ['&&', '||', ';', '|'];
const COMMENT_START = '#';
const QUOTES = new Set(["'", '"']);
const WHITESPACE = /\s/;
const PROMPT = /^\s*(?:\$|PS[^>]*>|>)\s+/;
const CALL_OPERATOR = '&';
const OPTION_PREFIX = '-';
const OPTION_VALUE_SEPARATOR = '=';
const SCRIPT_EXTENSION = /\.ps1$/i;
const EXECUTABLE_SUFFIX = /\.(?:exe|cmd)$/i;

const CHANGE_DIRECTORY_COMMANDS = new Set(['cd', 'set-location', 'push-location', 'pushd', 'sl']);
const NPM = 'npm';
const NODE = 'node';
const POWERSHELL_HOSTS = new Set(['powershell', 'pwsh']);
const NPM_RUN_COMMANDS = new Set(['run', 'run-script', 'rum', 'urn']);
const NPM_TEST_COMMANDS = new Set(['test', 'tst', 't']);
const NPM_TEST_SCRIPT = 'test';
const NPM_WORKSPACE_OPTIONS = new Set(['--workspace', '-w']);
const NPM_PREFIX_OPTIONS = new Set(['--prefix', '-C']);
const NODE_TEST_OPTION = '--test';
const NODE_INLINE_OPTIONS = new Set(['-e', '--eval', '-p', '--print', '-r', '--require', '--import', '--loader']);
const NODE_VALUE_OPTIONS = new Set(['--test-reporter', '--test-reporter-destination', '--test-name-pattern', '--test-concurrency']);
const POWERSHELL_FILE_OPTIONS = new Set(['-file', '-f']);
const POWERSHELL_COMMAND_OPTIONS = new Set(['-command', '-c']);
const SWITCH = /^-([A-Za-z][A-Za-z0-9]*)(?::.*)?$/;

/**
 * Splits a shell line into commands (on && || ; |), each a list of tokens. Quotes group words and are removed;
 * an unquoted "#" at a word start ends the line (PowerShell and bash comments). A leading prompt ("$ ", "PS> ") is
 * dropped.
 * @param {string} line
 * @returns {string[][]}
 */
export function tokenizeShellLine(line) {
  const text = line.replace(PROMPT, '');
  const commands = [];
  let tokens = [];
  let current = '';
  let quote = null;
  let started = false;
  const endToken = () => {
    if (started) tokens.push(current);
    current = '';
    started = false;
  };
  const endCommand = () => {
    endToken();
    if (tokens.length > 0) commands.push(tokens);
    tokens = [];
  };
  for (let index = 0; index < text.length; index += 1) {
    const char = text[index];
    if (quote !== null) {
      if (char === quote) quote = null;
      else current += char;
      continue;
    }
    if (QUOTES.has(char)) {
      quote = char;
      started = true;
      continue;
    }
    if (WHITESPACE.test(char)) {
      endToken();
      continue;
    }
    if (char === COMMENT_START && !started) break;
    const operator = SEGMENT_OPERATORS.find((candidate) => text.startsWith(candidate, index));
    if (operator !== undefined) {
      endCommand();
      index += operator.length - 1;
      continue;
    }
    current += char;
    started = true;
  }
  endCommand();
  return commands;
}

function commandName(token) {
  return token.replace(EXECUTABLE_SUFFIX, '').toLowerCase();
}

function analyzeNpm(tokens) {
  let workspace = null;
  let script = null;
  let runSeen = false;
  for (let index = 1; index < tokens.length; index += 1) {
    const token = tokens[index];
    const [optionName, inlineValue] = token.split(OPTION_VALUE_SEPARATOR, 2);
    if (NPM_WORKSPACE_OPTIONS.has(optionName) || NPM_PREFIX_OPTIONS.has(optionName)) {
      workspace = inlineValue ?? tokens[index + 1] ?? null;
      if (inlineValue === undefined) index += 1;
    } else if (token.startsWith(OPTION_PREFIX)) {
      continue;
    } else if (!runSeen && script === null && NPM_RUN_COMMANDS.has(token)) {
      runSeen = true;
    } else if (!runSeen && script === null && NPM_TEST_COMMANDS.has(token)) {
      script = NPM_TEST_SCRIPT;
    } else if (runSeen && script === null) {
      script = token;
    } else if (!runSeen && script === null) {
      return null;
    }
  }
  return script === null ? null : { kind: CommandKinds.npmScript, script, workspace };
}

function analyzeNode(tokens) {
  const rest = tokens.slice(1);
  if (rest.some((token) => NODE_INLINE_OPTIONS.has(token))) return null;
  if (rest.includes(NODE_TEST_OPTION)) {
    const patterns = [];
    for (let index = 0; index < rest.length; index += 1) {
      if (NODE_VALUE_OPTIONS.has(rest[index])) index += 1;
      else if (!rest[index].startsWith(OPTION_PREFIX)) patterns.push(rest[index]);
    }
    return { kind: CommandKinds.nodeTest, patterns };
  }
  const file = rest.find((token) => !token.startsWith(OPTION_PREFIX));
  return file === undefined ? null : { kind: CommandKinds.nodeScript, file };
}

function switchesOf(tokens) {
  return tokens.map((token) => SWITCH.exec(token)).filter(Boolean).map((match) => match[1]);
}

function analyzePowerShellHost(tokens) {
  for (let index = 1; index < tokens.length; index += 1) {
    const option = tokens[index].toLowerCase();
    if (POWERSHELL_FILE_OPTIONS.has(option)) {
      const file = tokens[index + 1];
      return file === undefined ? null : { kind: CommandKinds.powershellScript, file, switches: switchesOf(tokens.slice(index + 2)) };
    }
    if (POWERSHELL_COMMAND_OPTIONS.has(option)) {
      const inner = tokenizeShellLine(tokens.slice(index + 1).join(' '));
      return inner.length === 0 ? null : analyzeCommand(inner[0]);
    }
  }
  return null;
}

/**
 * Describes one tokenized command, or null when it is none of the kinds the gate checks.
 * @param {string[]} commandTokens
 */
export function analyzeCommand(commandTokens) {
  const tokens = commandTokens[0] === CALL_OPERATOR ? commandTokens.slice(1) : commandTokens;
  if (tokens.length === 0) return null;
  const name = commandName(tokens[0]);
  if (CHANGE_DIRECTORY_COMMANDS.has(name)) {
    const target = tokens.slice(1).find((token) => !token.startsWith(OPTION_PREFIX));
    return target === undefined ? null : { kind: CommandKinds.changeDirectory, target };
  }
  if (name === NPM) return analyzeNpm(tokens);
  if (name === NODE) return analyzeNode(tokens);
  if (POWERSHELL_HOSTS.has(name)) return analyzePowerShellHost(tokens);
  if (SCRIPT_EXTENSION.test(tokens[0])) {
    return { kind: CommandKinds.powershellScript, file: tokens[0], switches: switchesOf(tokens.slice(1)) };
  }
  return null;
}

/** Every recognised command on a shell line. */
export function analyzeShellLine(line) {
  return tokenizeShellLine(line).map(analyzeCommand).filter(Boolean);
}
