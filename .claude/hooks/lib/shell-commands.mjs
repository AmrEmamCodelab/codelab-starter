// Per-command knowledge for the shell-write detector: which commands write files, where, and which commands run a
// nested script whose writes cannot be seen. shell-writes.mjs lexes the command line; this module classifies one
// simple command (its canonical name and arguments). Every handler returns { targets, nested, opaque }:
//   targets  [{ kind, raw, content }]  files written (raw = the path as written, or null when not statically known)
//   nested   string[]                  command strings run by this command (bash -c, cmd /c, eval, iex...)
//   opaque   string | null             a reason the command may write files the detector cannot name

export const WriteKinds = Object.freeze({
  Redirect: 'redirect',
  Tee: 'tee',
  InPlaceEdit: 'in-place edit',
  PowerShellCmdlet: 'PowerShell cmdlet',
  DotnetFileApi: '.NET file API',
  CopyOrMove: 'copy/move',
  Remove: 'remove',
  Download: 'download',
  GitWorktree: 'git worktree write',
});

const NO_WRITES = Object.freeze({ targets: [], nested: [], opaque: null });

function result({ targets = [], nested = [], opaque = null } = {}) {
  return { targets, nested, opaque };
}

function isOption(arg) {
  return arg.startsWith('-') && arg.length > 1;
}

function positional(args) {
  return args.filter((arg) => !isOption(arg));
}

// ---------------------------------------------------------------------------------------------------------------
// POSIX tools
// ---------------------------------------------------------------------------------------------------------------

const InPlaceEditors = Object.freeze({
  sed: { inPlaceFlag: /^(-[a-zA-Z]*i\S*|--in-place\S*)$/, scriptOptions: new Set(['-e', '-f', '--expression', '--file']) },
  perl: { inPlaceFlag: /^-[a-zA-Z]*i\S*$/, scriptOptions: new Set(['-e', '-E']) },
  ruby: { inPlaceFlag: /^-[a-zA-Z]*i\S*$/, scriptOptions: new Set(['-e']) },
});
const AWK_IN_PLACE = /^(?:-i|--include)$/;
const AWK_IN_PLACE_LIBRARY = 'inplace';

function inPlaceEditTargets(editor, args) {
  if (!args.some((arg) => editor.inPlaceFlag.test(arg))) return [];
  const files = [];
  let scriptGiven = false;
  for (let index = 0; index < args.length; index += 1) {
    const arg = args[index];
    if (editor.scriptOptions.has(arg)) { scriptGiven = true; index += 1; continue; }
    if (isOption(arg)) continue;
    if (!scriptGiven) { scriptGiven = true; continue; }
    files.push(arg);
  }
  return files;
}

function inPlaceEditor(name) {
  return (args) => {
    const files = inPlaceEditTargets(InPlaceEditors[name], args);
    const script = name === 'sed' ? null : interpreterScript(args, InPlaceEditors[name].scriptOptions);
    const opaque = script && writesFromScript(script) ? `${name} -e script writes files` : null;
    return result({ targets: files.map((raw) => ({ kind: `${name} ${WriteKinds.InPlaceEdit}`, raw, content: null })), opaque });
  };
}

function awk(args) {
  const inPlace = args.some((arg, index) => AWK_IN_PLACE.test(arg) && args[index + 1] === AWK_IN_PLACE_LIBRARY);
  if (!inPlace) return NO_WRITES;
  const files = positional(args).filter((arg) => arg !== AWK_IN_PLACE_LIBRARY).slice(1);
  return result({ targets: files.map((raw) => ({ kind: `awk ${WriteKinds.InPlaceEdit}`, raw, content: null })) });
}

const TARGET_DIRECTORY_OPTIONS = new Set(['-t', '--target-directory']);
const TARGET_DIRECTORY_PREFIX = '--target-directory=';

/** cp/mv/install/ln/rsync: the destination is -t DIR, else the last operand. */
function copyLike(name) {
  return (args) => {
    for (let index = 0; index < args.length; index += 1) {
      if (TARGET_DIRECTORY_OPTIONS.has(args[index])) return result({ targets: [{ kind: `${name} ${WriteKinds.CopyOrMove}`, raw: args[index + 1] ?? null, content: null }] });
      if (args[index].startsWith(TARGET_DIRECTORY_PREFIX)) {
        return result({ targets: [{ kind: `${name} ${WriteKinds.CopyOrMove}`, raw: args[index].slice(TARGET_DIRECTORY_PREFIX.length), content: null }] });
      }
    }
    const operands = positional(args);
    if (operands.length === 0) return NO_WRITES;
    const sources = name === 'mv' ? operands.slice(0, -1) : [];
    return result({
      targets: [...sources, operands.at(-1)].map((raw) => ({ kind: `${name} ${WriteKinds.CopyOrMove}`, raw, content: null })),
    });
  };
}

function removeLike(name) {
  return (args) => result({ targets: positional(args).map((raw) => ({ kind: `${name} ${WriteKinds.Remove}`, raw, content: null })) });
}

const DD_OUTPUT = /^of=(.+)$/;

function dd(args) {
  const output = args.map((arg) => DD_OUTPUT.exec(arg)).find(Boolean);
  return output ? result({ targets: [{ kind: `dd ${WriteKinds.Download}`, raw: output[1], content: null }] }) : NO_WRITES;
}

function optionValues(args, options, prefixes = []) {
  const values = [];
  for (let index = 0; index < args.length; index += 1) {
    if (options.has(args[index]) || options.has(args[index].toLowerCase())) values.push(args[index + 1] ?? null);
    const prefix = prefixes.find((candidate) => args[index].startsWith(candidate));
    if (prefix) values.push(args[index].slice(prefix.length));
  }
  return values;
}

function downloader(name, options, prefixes) {
  return (args) => result({
    targets: optionValues(args, options, prefixes).map((raw) => ({ kind: `${name} ${WriteKinds.Download}`, raw, content: null })),
  });
}

const GIT_PATHSPEC_SEPARATOR = '--';
const GIT_OPAQUE_SUBCOMMANDS = new Set(['apply', 'am', 'merge', 'rebase', 'cherry-pick', 'revert', 'pull', 'reset', 'stash', 'switch']);
const GIT_PATH_SUBCOMMANDS = new Set(['checkout', 'restore']);

function git(args) {
  const subcommand = positional(args)[0];
  if (GIT_OPAQUE_SUBCOMMANDS.has(subcommand)) {
    return subcommand === 'stash' && !['pop', 'apply'].includes(positional(args)[1])
      ? NO_WRITES
      : result({ opaque: `git ${subcommand} rewrites working-tree files` });
  }
  if (!GIT_PATH_SUBCOMMANDS.has(subcommand)) return NO_WRITES;
  const separator = args.indexOf(GIT_PATHSPEC_SEPARATOR);
  const paths = separator >= 0 ? args.slice(separator + 1) : subcommand === 'restore' ? positional(args).slice(1) : [];
  return result({ targets: paths.map((raw) => ({ kind: `git ${subcommand} ${WriteKinds.GitWorktree}`, raw, content: null })) });
}

// ---------------------------------------------------------------------------------------------------------------
// Interpreters and command runners
// ---------------------------------------------------------------------------------------------------------------

const INTERPRETER_SCRIPT_OPTIONS = new Set(['-c', '-e', '-E', '--eval', '-p', '--print', '-r']);
// File-writing APIs of the common script languages (Node fs, Python open/pathlib/shutil/os, Perl/Ruby open/File).
const SCRIPT_WRITE_API = /writeFile|appendFile|createWriteStream|copyFile|\brename(?:Sync)?\s*\(|\bunlink|\brmSync|\brm\s*\(|\bopen\s*\([^)]*['"][wax+]|['"]\s*[>+]|\.write_(?:text|bytes)|\bshutil\.|\bos\.(?:rename|remove|replace|unlink)|File\.(?:write|open|delete|rename)|IO\.write|Set-Content|Out-File/i;

function interpreterScript(args, scriptOptions = INTERPRETER_SCRIPT_OPTIONS) {
  const index = args.findIndex((arg) => scriptOptions.has(arg));
  return index >= 0 ? args[index + 1] ?? '' : null;
}

/** True when an inline script calls a file-writing API. */
export function writesFromScript(script) {
  return SCRIPT_WRITE_API.test(script);
}

function interpreter(name) {
  return (args) => {
    const script = interpreterScript(args);
    return script !== null && writesFromScript(script) ? result({ opaque: `${name} inline script writes files` }) : NO_WRITES;
  };
}

const SHELL_SCRIPT_FLAG = /^-[a-zA-Z]*c[a-zA-Z]*$/;

function posixShell(args) {
  const index = args.findIndex((arg) => SHELL_SCRIPT_FLAG.test(arg));
  return index >= 0 ? result({ nested: [args[index + 1] ?? ''] }) : NO_WRITES;
}

const POWERSHELL_COMMAND_OPTIONS = new Set(['-command', '-c']);
const POWERSHELL_ENCODED_OPTIONS = new Set(['-encodedcommand', '-enc', '-ec', '-e']);
const POWERSHELL_ENCODED_CHARSET = 'utf16le';

function powershellHost(args) {
  const lower = args.map((arg) => arg.toLowerCase());
  const commandIndex = lower.findIndex((arg) => POWERSHELL_COMMAND_OPTIONS.has(arg));
  if (commandIndex >= 0) return result({ nested: [args.slice(commandIndex + 1).join(' ')] });
  const encodedIndex = lower.findIndex((arg) => POWERSHELL_ENCODED_OPTIONS.has(arg));
  if (encodedIndex < 0) return NO_WRITES;
  const decoded = Buffer.from(args[encodedIndex + 1] ?? '', 'base64').toString(POWERSHELL_ENCODED_CHARSET);
  return result({ nested: [decoded] });
}

const CMD_RUN_SWITCH = /^\/[ck]$/i;

function cmd(args) {
  const index = args.findIndex((arg) => CMD_RUN_SWITCH.test(arg));
  return index >= 0 ? result({ nested: [args.slice(index + 1).join(' ')] }) : NO_WRITES;
}

function evaluate(args) {
  return result({ nested: [args.join(' ')] });
}

const ENV_OPTIONS_WITH_VALUE = new Set(['-u', '--unset', '-C', '--chdir', '-S', '--split-string']);
const ENV_ASSIGNMENT = /^[A-Za-z_][A-Za-z0-9_]*=/;

/** env [options] [NAME=value...] command args: the command runs as if typed directly. */
function envRunner(args) {
  let index = 0;
  while (index < args.length && (isOption(args[index]) || ENV_ASSIGNMENT.test(args[index]))) {
    index += ENV_OPTIONS_WITH_VALUE.has(args[index]) ? 2 : 1;
  }
  return result({ nested: [quoteWords(args.slice(index))] });
}

/** xargs/find -exec supply file names at run time: a writing inner command has targets nobody can name. */
function runnerOfUnknownFiles(name, innerWords) {
  if (innerWords.length === 0) return NO_WRITES;
  const inner = classifyCommand(innerWords[0], innerWords.slice(1));
  const writes = inner.targets.length > 0 || inner.opaque !== null || WRITE_CAPABLE.has(canonicalName(innerWords[0]));
  return result({ nested: inner.nested, opaque: writes ? `${name} runs a writing command on files chosen at run time` : null });
}

const XARGS_OPTIONS_WITH_VALUE = new Set(['-I', '-n', '-P', '-L', '-s', '-d', '-E', '-a']);

function xargs(args) {
  let index = 0;
  while (index < args.length && isOption(args[index])) index += XARGS_OPTIONS_WITH_VALUE.has(args[index]) ? 2 : 1;
  return runnerOfUnknownFiles('xargs', args.slice(index));
}

const FIND_EXEC = new Set(['-exec', '-execdir', '-ok', '-okdir']);
const FIND_DELETE = '-delete';

function find(args) {
  if (args.includes(FIND_DELETE)) return result({ opaque: 'find -delete removes files chosen at run time' });
  const index = args.findIndex((arg) => FIND_EXEC.has(arg));
  return index >= 0 ? runnerOfUnknownFiles('find -exec', args.slice(index + 1).filter((arg) => arg !== ';' && arg !== '+')) : NO_WRITES;
}

function quoteWords(words) {
  return words.map((word) => (/[\s"]/.test(word) ? `'${word}'` : word)).join(' ');
}

// ---------------------------------------------------------------------------------------------------------------
// PowerShell cmdlets (aliases resolve to the cmdlet name)
// ---------------------------------------------------------------------------------------------------------------

const PS_SWITCHES = new Set([
  '-force', '-recurse', '-append', '-noclobber', '-nonewline', '-passthru', '-whatif', '-confirm', '-asbytestream',
  '-container', '-usebasicparsing', '-noprofile', '-noninteractive',
]);
const PS_PATH_NAMES = ['path', 'filepath', 'literalpath', 'pspath', 'lp'];
const PS_PARAMETER_WITH_VALUE = /^(-[a-z]+):(.*)$/i;

/** Splits PowerShell arguments into named parameters (lower-case, colon syntax supported) and positionals. */
export function parsePowerShellArgs(args) {
  const named = new Map();
  const positionals = [];
  for (let index = 0; index < args.length; index += 1) {
    const arg = args[index];
    const colon = PS_PARAMETER_WITH_VALUE.exec(arg);
    if (colon) { named.set(colon[1].slice(1).toLowerCase(), colon[2]); continue; }
    if (!isOption(arg)) { positionals.push(arg); continue; }
    const name = arg.toLowerCase();
    if (PS_SWITCHES.has(name)) continue;
    named.set(name.slice(1), args[index + 1] ?? null);
    index += 1;
  }
  return { named, positionals };
}

function namedValue(named, names) {
  const name = names.find((candidate) => named.has(candidate));
  return name ? named.get(name) : undefined;
}

function cmdletWrite(cmdlet, raw, content = null) {
  return { kind: `${WriteKinds.PowerShellCmdlet} ${cmdlet}`, raw, content };
}

function psContentWriter(cmdlet) {
  return (args) => {
    const { named, positionals } = parsePowerShellArgs(args);
    const path = namedValue(named, PS_PATH_NAMES);
    const raw = path !== undefined ? path : positionals.shift() ?? null;
    const content = named.get('value') ?? named.get('inputobject') ?? positionals[0] ?? null;
    return result({ targets: [cmdletWrite(cmdlet, raw, content)] });
  };
}

const PS_TEE_PATH_OPTIONS = new Set(['-filepath', '-literalpath', '-path']);

/** POSIX tee (every operand is a file) or the PowerShell tee alias of Tee-Object (-FilePath). */
function tee(args) {
  if (args.some((arg) => PS_TEE_PATH_OPTIONS.has(arg.toLowerCase().split(':')[0]))) return psContentWriter('Tee-Object')(args);
  return result({ targets: positional(args).map((raw) => ({ kind: WriteKinds.Tee, raw, content: null })) });
}

function psNewItem(args) {
  const { named, positionals } = parsePowerShellArgs(args);
  const path = namedValue(named, PS_PATH_NAMES) ?? positionals[0] ?? null;
  const name = named.get('name');
  const raw = path && name ? `${path}/${name}` : path ?? name ?? null;
  return result({ targets: [cmdletWrite('New-Item', raw, named.get('value') ?? null)] });
}

function psDestination(cmdlet, destinationNames) {
  return (args) => {
    const { named, positionals } = parsePowerShellArgs(args);
    const destination = namedValue(named, destinationNames);
    const raw = destination !== undefined ? destination : positionals[1] ?? null;
    return result({ targets: [cmdletWrite(cmdlet, raw)] });
  };
}

function psMove(args) {
  const { named, positionals } = parsePowerShellArgs(args);
  const source = namedValue(named, PS_PATH_NAMES) ?? positionals[0] ?? null;
  const destination = namedValue(named, ['destination']) ?? positionals[1] ?? null;
  return result({ targets: [cmdletWrite('Move-Item', source), cmdletWrite('Move-Item', destination)] });
}

function psPathTargets(cmdlet) {
  return (args) => {
    const { named, positionals } = parsePowerShellArgs(args);
    const path = namedValue(named, PS_PATH_NAMES);
    const raws = path !== undefined ? [path] : positionals.length > 0 ? positionals : [null];
    return result({ targets: raws.map((raw) => cmdletWrite(cmdlet, raw)) });
  };
}

function psDownload(cmdlet) {
  return (args) => {
    const outFile = parsePowerShellArgs(args).named.get('outfile');
    return outFile === undefined ? NO_WRITES : result({ targets: [cmdletWrite(cmdlet, outFile)] });
  };
}

// ---------------------------------------------------------------------------------------------------------------
// Dispatch
// ---------------------------------------------------------------------------------------------------------------

const ALIASES = Object.freeze({
  sc: 'set-content', ac: 'add-content', clc: 'clear-content',
  cpi: 'copy-item', copy: 'copy-item',
  mi: 'move-item', move: 'move-item',
  ren: 'rename-item', rni: 'rename-item',
  ri: 'remove-item', del: 'remove-item', erase: 'remove-item', rd: 'remove-item',
  ni: 'new-item', iwr: 'invoke-webrequest', irm: 'invoke-restmethod',
  iex: 'invoke-expression', python3: 'python', py: 'python', pwsh: 'powershell', zsh: 'bash', sh: 'bash',
  pushd: 'cd', chdir: 'cd', 'set-location': 'cd', sl: 'cd', 'push-location': 'cd',
});

const HANDLERS = Object.freeze({
  tee,
  'tee-object': psContentWriter('Tee-Object'),
  'set-content': psContentWriter('Set-Content'),
  'add-content': psContentWriter('Add-Content'),
  'out-file': psContentWriter('Out-File'),
  'export-csv': psContentWriter('Export-Csv'),
  'export-clixml': psContentWriter('Export-Clixml'),
  'clear-content': psPathTargets('Clear-Content'),
  'remove-item': psPathTargets('Remove-Item'),
  'rename-item': psPathTargets('Rename-Item'),
  'new-item': psNewItem,
  'copy-item': psDestination('Copy-Item', ['destination']),
  'move-item': psMove,
  'expand-archive': psDestination('Expand-Archive', ['destinationpath']),
  'invoke-webrequest': psDownload('Invoke-WebRequest'),
  'invoke-restmethod': psDownload('Invoke-RestMethod'),
  'invoke-expression': evaluate,
  sed: inPlaceEditor('sed'),
  perl: inPlaceEditor('perl'),
  ruby: inPlaceEditor('ruby'),
  awk,
  gawk: awk,
  cp: copyLike('cp'),
  mv: copyLike('mv'),
  install: copyLike('install'),
  ln: copyLike('ln'),
  rsync: copyLike('rsync'),
  rm: removeLike('rm'),
  rmdir: removeLike('rmdir'),
  unlink: removeLike('unlink'),
  shred: removeLike('shred'),
  truncate: removeLike('truncate'),
  dd,
  // curl/wget are also PowerShell 5.1 aliases of Invoke-WebRequest, hence -outfile.
  curl: downloader('curl', new Set(['-o', '--output', '-outfile']), ['--output=']),
  wget: downloader('wget', new Set(['-O', '--output-document', '-outfile']), ['--output-document=']),
  git,
  patch: () => result({ opaque: 'patch writes the files named inside the patch' }),
  python: interpreter('python'),
  node: interpreter('node'),
  deno: interpreter('deno'),
  bun: interpreter('bun'),
  bash: posixShell,
  powershell: powershellHost,
  cmd,
  eval: evaluate,
  env: envRunner,
  xargs,
  find,
});

/** Commands that write when xargs/find hand them file names. */
const WRITE_CAPABLE = new Set([
  'cp', 'mv', 'install', 'ln', 'rsync', 'rm', 'rmdir', 'unlink', 'shred', 'truncate', 'tee', 'dd',
  'set-content', 'add-content', 'out-file', 'copy-item', 'move-item', 'remove-item', 'rename-item', 'new-item',
]);

const EXECUTABLE_SUFFIX = /\.exe$/i;
const POWERSHELL_ESCAPE = /`/g;

/** The canonical command name: path and .exe removed, PowerShell backtick escapes removed, aliases resolved. */
export function canonicalName(word) {
  const base = (word ?? '').replace(POWERSHELL_ESCAPE, '').replace(/\\/g, '/').split('/').at(-1) ?? '';
  const name = base.replace(EXECUTABLE_SUFFIX, '').toLowerCase();
  return ALIASES[name] ?? name;
}

/** Classifies one simple command. */
export function classifyCommand(commandWord, args) {
  const handler = HANDLERS[canonicalName(commandWord)];
  return handler ? handler(args) : NO_WRITES;
}

/** True for cd, pushd, Set-Location and their aliases. */
export function isChangeDirectory(commandWord) {
  return canonicalName(commandWord) === 'cd';
}
