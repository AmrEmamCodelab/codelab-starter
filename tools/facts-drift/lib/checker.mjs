// Runs every facts-drift check over the documents in scope and applies the allowlist.
import { readFileSync } from 'node:fs';
import { join, posix } from 'node:path';
import { matchesGlob } from '../../extract/lib/glob.mjs';
import { createFinding } from '../../extract/lib/findings.mjs';
import { loadScrubConfiguration, scrubFile } from '../../extract/lib/scrubber.mjs';
import { CommandKinds, analyzeCommand, tokenizeShellLine } from './commands.mjs';
import { ruleIdSelected, subjectSelected } from './config.mjs';
import {
  PACKAGE_JSON, PATH_SEPARATOR, RuleIds, SCRUB_EXCEPTIONS_FILE, SCRUB_TERM_RULE_PREFIX, SCRUB_TERMS_FILE,
} from './constants.mjs';
import { readCodeSpans, readMarkdownLines, readTableColumns } from './markdown.mjs';
import { checkPathCandidate, classifyPathCandidate, topLevelEntriesOf } from './paths.mjs';
import { readScriptParameters, switchResolves } from './powershell-params.mjs';
import { checkNamedReference, findNamedReferences } from './references.mjs';
import {
  anchoredCandidates, buildRepoIndex, entryExists, globMatches, nearestDirectoryWith,
} from './repo-index.mjs';
import { buildSymbolIndex, classifyTypeCandidate, typeResolves } from './symbols.mjs';

const ROOT_DIRECTORY = '';
const COLUMN_BASE = 1;
const BACKSLASH = '\\';
const GLOB_CHARACTERS = /[*?]/;
/** Placeholders, variables and absolute or home paths: a command argument the gate cannot (and must not) resolve. */
const UNRESOLVABLE_ARGUMENT = /<[^>]*>|\{[^}]*\}|\.\.\.|\$|%|^~|^\/|:/;
/** Placeholders and variables in an npm script or workspace name (a ':' is legal there: 'api:gen'). */
const PLACEHOLDER_ARGUMENT = /<[^>]*>|\{[^}]*\}|\.\.\.|\$|%/;
const SPACE = /\s/;
const INTRO_FOLDER = /\b(?:from|in|inside|under)\s+(?:the\s+)?`([^`]+)`/gi;
const MAX_STALE_EXCERPT = 120;

function toRepoPath(text) {
  return text.replaceAll(BACKSLASH, PATH_SEPARATOR).replace(/^\.\//, '');
}

function isInScope(path, scope) {
  return scope.include.some((glob) => matchesGlob(path, glob, { anchored: true }))
    && !scope.exclude.some((glob) => matchesGlob(path, glob, { anchored: true }));
}

/** Lazily read, cached per run: package.json manifests (scripts, names) and PowerShell parameter blocks. */
function createFileCache(index) {
  const manifests = new Map();
  const parameters = new Map();
  let directoriesByName = null;
  const manifest = (directory) => {
    if (!manifests.has(directory)) {
      const file = packageJsonOf(directory);
      let parsed = null;
      if (index.fileSet.has(file)) {
        try {
          parsed = JSON.parse(readFileSync(join(index.root, file), 'utf8'));
        } catch {
          // An unreadable package.json has no name or scripts the gate can vouch for.
          parsed = {};
        }
      }
      manifests.set(directory, parsed);
    }
    return manifests.get(directory);
  };
  return {
    npmScripts(directory) {
      const parsed = manifest(directory);
      return parsed === null ? null : new Set(Object.keys(parsed.scripts ?? {}));
    },
    packageDirectoryByName(name) {
      if (directoriesByName === null) {
        directoriesByName = new Map();
        for (const file of index.files.filter((candidate) => posix.basename(candidate) === PACKAGE_JSON)) {
          const directory = posix.dirname(file) === '.' ? ROOT_DIRECTORY : posix.dirname(file);
          const packageName = manifest(directory)?.name;
          if (typeof packageName === 'string' && !directoriesByName.has(packageName)) directoriesByName.set(packageName, directory);
        }
      }
      return directoriesByName.get(name) ?? null;
    },
    scriptParameters(file) {
      if (!parameters.has(file)) parameters.set(file, readScriptParameters(readFileSync(join(index.root, file), 'utf8')));
      return parameters.get(file);
    },
  };
}

/** The first anchored candidate (working folder, root, document folder, then folders named on the line) that is an existing file. */
function resolveFile(index, reference, context) {
  const path = toRepoPath(reference);
  const anchors = [context, ...context.folderHints.map((hint) => ({ ...context, workingDirectory: hint }))];
  for (const anchor of anchors) {
    const found = anchoredCandidates(path, anchor).find((candidate) => index.fileSet.has(candidate));
    if (found !== undefined) return found;
  }
  return null;
}

function packageJsonOf(directory) {
  return directory === ROOT_DIRECTORY ? PACKAGE_JSON : `${directory}${PATH_SEPARATOR}${PACKAGE_JSON}`;
}

/** "-w frontend/starter-web" (a folder) or "-w @codelab/ng-dga" (a package name). */
function workspaceDirectory(index, cache, workspace, context) {
  const byName = cache.packageDirectoryByName(workspace);
  if (byName !== null) return byName;
  return anchoredCandidates(toRepoPath(workspace), context).find((candidate) => index.fileSet.has(packageJsonOf(candidate))) ?? null;
}

/**
 * Checks one recognised command. Returns problems as { ruleId, message, subject }.
 * context: { documentDirectory, workingDirectory, folderHints: string[] (existing folders named on the same line) }
 */
function checkCommand(index, cache, command, context, config) {
  const problems = [];
  switch (command.kind) {
    case CommandKinds.npmScript: {
      if (PLACEHOLDER_ARGUMENT.test(command.script)) break;
      if (command.workspace !== null && PLACEHOLDER_ARGUMENT.test(command.workspace)) break;
      const directory = command.workspace === null
        ? nearestDirectoryWith(index, context.workingDirectory ?? context.documentDirectory, PACKAGE_JSON)
        : workspaceDirectory(index, cache, command.workspace, context);
      if (directory === null) {
        problems.push({ ruleId: RuleIds.npmPackageMissing, subject: command.workspace ?? command.script, message: `no package.json for 'npm run ${command.script}'${command.workspace ? ` in workspace '${command.workspace}'` : ''}` });
        break;
      }
      const hinted = command.workspace === null
        ? context.folderHints.map((hint) => nearestDirectoryWith(index, hint, PACKAGE_JSON)).filter((hint) => hint !== null)
        : [];
      if (![directory, ...hinted].some((candidate) => cache.npmScripts(candidate)?.has(command.script))) {
        problems.push({ ruleId: RuleIds.npmScriptMissing, subject: command.script, message: `npm script '${command.script}' is not defined in ${packageJsonOf(directory)}` });
      }
      break;
    }
    case CommandKinds.nodeScript: {
      if (UNRESOLVABLE_ARGUMENT.test(command.file)) break;
      if (resolveFile(index, command.file, context) === null) {
        problems.push({ ruleId: RuleIds.commandFileMissing, subject: toRepoPath(command.file), message: `'node ${command.file}': no such file` });
      }
      break;
    }
    case CommandKinds.nodeTest: {
      for (const pattern of command.patterns) {
        if (UNRESOLVABLE_ARGUMENT.test(pattern)) continue;
        const path = toRepoPath(pattern);
        const resolves = GLOB_CHARACTERS.test(path)
          ? globMatches(index, path, { ...context, allowSuffix: false })
          : anchoredCandidates(path, context).some((candidate) => entryExists(index, candidate));
        if (!resolves) problems.push({ ruleId: RuleIds.globEmpty, subject: path, message: `'node --test ${pattern}' matches no test file` });
      }
      break;
    }
    case CommandKinds.powershellScript: {
      if (UNRESOLVABLE_ARGUMENT.test(command.file) || GLOB_CHARACTERS.test(command.file)) break;
      const file = resolveFile(index, command.file, context);
      if (file === null) {
        problems.push({ ruleId: RuleIds.commandFileMissing, subject: toRepoPath(command.file), message: `PowerShell script '${command.file}': no such file` });
        break;
      }
      const declared = cache.scriptParameters(file);
      const accepted = new Set([...(declared ?? []), ...config.powershellCommonParameters]);
      for (const name of command.switches) {
        if (!switchResolves(name, accepted)) {
          problems.push({ ruleId: RuleIds.scriptSwitchUnknown, subject: `-${name}`, message: `'${file}' has no parameter -${name}${declared === null ? ' (it declares no param block)' : ''}` });
        }
      }
      break;
    }
    default:
      break;
  }
  return problems;
}

/** Existing folders named by the other code spans of a line ("run `npm run lint` in `frontend/starter-web`"). */
function folderHintsOn(index, spans, documentDirectory) {
  const hints = new Set();
  for (const span of spans) {
    const text = span.content.trim();
    if (SPACE.test(text) || UNRESOLVABLE_ARGUMENT.test(text)) continue;
    for (const candidate of anchoredCandidates(toRepoPath(text), { documentDirectory })) {
      if (candidate !== ROOT_DIRECTORY && entryExists(index, candidate, { directoryOnly: true })) hints.add(candidate);
    }
  }
  return [...hints];
}

/**
 * The working folder a code block's intro names ("Then, from `frontend/starter-web`:", "in `tools/kit`"): the last
 * such folder that exists, else null (commands then run from the root or the document's folder).
 */
function introFolder(index, intro, documentDirectory) {
  let folder = null;
  for (const match of intro.matchAll(INTRO_FOLDER)) {
    const text = match[1].trim();
    if (UNRESOLVABLE_ARGUMENT.test(text)) continue;
    const found = anchoredCandidates(toRepoPath(text), { documentDirectory })
      .find((candidate) => candidate !== ROOT_DIRECTORY && entryExists(index, candidate, { directoryOnly: true }));
    if (found !== undefined) folder = found;
  }
  return folder;
}

function isCommandSpan(content) {
  return SPACE.test(content.trim()) && analyzeCommand(tokenizeShellLine(content)[0] ?? []) !== null;
}

/**
 * Checks one Markdown document.
 * @returns {{ path, line, column, ruleId, message, subject }[]} raw findings (allowlist not applied)
 */
export function checkDocument(path, text, context) {
  const { index, cache, symbols, config, staleTerms, topLevelEntries } = context;
  const documentDirectory = posix.dirname(path) === '.' ? ROOT_DIRECTORY : posix.dirname(path);
  const lines = readMarkdownLines(text);
  const tableRows = readTableColumns(lines);
  const findings = [];
  const add = (line, column, problem) => findings.push({ path, line, column, ...problem });
  let currentBlock = null;
  let workingDirectory = null;

  for (const line of lines) {
    for (const rule of staleTerms) {
      for (const match of line.text.matchAll(rule.pattern)) {
        add(line.number, match.index + COLUMN_BASE, { ruleId: rule.id, subject: match[0], message: `${rule.message}: '${match[0]}'` });
      }
    }

    if (line.fence !== null) {
      if (!config.shellFenceLanguages.has(line.fence.language)) continue;
      if (currentBlock !== line.fence.id) {
        currentBlock = line.fence.id;
        workingDirectory = introFolder(index, line.fence.intro, documentDirectory);
      }
      for (const tokens of tokenizeShellLine(line.text)) {
        const command = analyzeCommand(tokens);
        if (command === null) continue;
        const commandContext = { documentDirectory, workingDirectory, folderHints: [] };
        if (command.kind === CommandKinds.changeDirectory) {
          if (UNRESOLVABLE_ARGUMENT.test(command.target)) continue;
          const target = anchoredCandidates(toRepoPath(command.target), commandContext)
            .find((candidate) => entryExists(index, candidate, { directoryOnly: true }));
          if (target !== undefined) workingDirectory = target;
          continue;
        }
        for (const problem of checkCommand(index, cache, command, commandContext, config)) add(line.number, COLUMN_BASE, problem);
      }
      continue;
    }

    const spans = readCodeSpans(line.text);
    const folderHints = folderHintsOn(index, spans, documentDirectory);
    for (const span of spans) {
      const column = span.start + COLUMN_BASE;
      if (isCommandSpan(span.content)) {
        for (const tokens of tokenizeShellLine(span.content)) {
          const command = analyzeCommand(tokens);
          if (command === null || command.kind === CommandKinds.changeDirectory) continue;
          for (const problem of checkCommand(index, cache, command, { documentDirectory, workingDirectory: null, folderHints }, config)) {
            add(line.number, column, problem);
          }
        }
        continue;
      }
      const pathCandidate = classifyPathCandidate(span.content, { extensions: config.pathExtensions, topLevelEntries });
      if (pathCandidate !== null) {
        const problem = checkPathCandidate(index, pathCandidate, { documentDirectory });
        if (problem !== null) add(line.number, column, { ...problem, subject: pathCandidate.path });
        continue;
      }
      const typeCandidate = classifyTypeCandidate(span.content, config.types);
      if (typeCandidate !== null && !typeResolves(typeCandidate, symbols, config.types.externalSymbols)) {
        add(line.number, column, {
          ruleId: RuleIds.typeUnresolved,
          subject: typeCandidate.display,
          message: `type '${typeCandidate.display}' is not declared in ${config.types.sourceRoots.join(', ')} and is not a listed external symbol`,
        });
      }
    }
    for (const reference of findNamedReferences(line.text, spans, tableRows.get(line.number), config.references)) {
      const problem = checkNamedReference(index, reference, config.references);
      if (problem !== null) add(line.number, reference.column, { ...problem, subject: reference.name });
    }
  }
  return findings;
}

/** Source-product terms from the scrub sweep's own list (tools/extract/modhs-terms.txt), on the documents in scope. */
function loadScrubTermConfiguration() {
  const configuration = loadScrubConfiguration({ termsFile: SCRUB_TERMS_FILE, exceptionsFile: SCRUB_EXCEPTIONS_FILE });
  return { ...configuration, rules: configuration.rules.filter((rule) => rule.id.startsWith(SCRUB_TERM_RULE_PREFIX)) };
}

function scrubTermFindings(path, text, scrubConfiguration) {
  return scrubFile(path, text, scrubConfiguration).map((finding) => ({
    path: finding.path, line: finding.line, column: finding.column, ruleId: finding.ruleId,
    message: `${finding.message}; source-product names never belong in agent, skill or doc text`,
    subject: finding.excerpt.slice(0, MAX_STALE_EXCERPT),
  }));
}

function findingKey(finding) {
  return [finding.path, finding.line, finding.column, finding.ruleId, finding.subject].join('\u0000');
}

/**
 * Applies the allowlist. Returns kept findings, suppressed findings (with the entry that suppressed them) and the
 * entries that suppressed nothing.
 */
export function applyAllowlist(findings, allowlist) {
  const used = new Set();
  const kept = [];
  const suppressed = [];
  for (const finding of findings) {
    const entry = allowlist.find((candidate) =>
      ruleIdSelected(candidate.ruleId, finding.ruleId)
      && matchesGlob(finding.path, candidate.fileGlob, { anchored: true })
      && subjectSelected(candidate.subject, finding.subject));
    if (entry === undefined) {
      kept.push(finding);
    } else {
      used.add(entry);
      suppressed.push({ ...finding, allowlistLine: entry.line, reason: entry.reason });
    }
  }
  return { kept, suppressed, unused: allowlist.filter((entry) => !used.has(entry)) };
}

/**
 * Runs the gate.
 * @param {string} root absolute repository root
 * @param {{ config, staleTerms, allowlist }} configuration from loadConfiguration
 * @param {{ index?: object, includeScrubTerms?: boolean }} [options] index: a prebuilt repo index (tests)
 */
export function checkRepository(root, { config, staleTerms, allowlist }, { index = buildRepoIndex(root), includeScrubTerms = true } = {}) {
  const cache = createFileCache(index);
  const symbols = buildSymbolIndex(index, config.types);
  const topLevelEntries = topLevelEntriesOf(index);
  const scrubConfiguration = includeScrubTerms ? loadScrubTermConfiguration() : null;
  const documents = index.files.filter((path) => isInScope(path, config.scope));
  const context = { index, cache, symbols, config, staleTerms, topLevelEntries };
  const seen = new Set();
  const raw = [];
  for (const path of documents) {
    const text = readFileSync(join(root, path), 'utf8');
    const documentFindings = checkDocument(path, text, context);
    if (scrubConfiguration !== null) documentFindings.push(...scrubTermFindings(path, text, scrubConfiguration));
    for (const finding of documentFindings) {
      const key = findingKey(finding);
      if (seen.has(key)) continue;
      seen.add(key);
      raw.push({ ...createFinding({ ...finding, excerpt: finding.subject }), subject: finding.subject });
    }
  }
  const { kept, suppressed, unused } = applyAllowlist(raw, allowlist);
  return { findings: kept, suppressed, unusedAllowlist: unused, fileCount: documents.length, documents };
}
