// Run: node --test "tools/facts-drift/tests/**/*.test.mjs"
// Classification and resolution units: paths, types, skill/agent references, configuration and allowlist parsing.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { parseAllowlist, parseConfig, parseStaleTerms, ruleIdSelected, subjectSelected } from '../lib/config.mjs';
import { ReferenceKinds, RuleIds } from '../lib/constants.mjs';
import { readCodeSpans, readMarkdownLines, readTableColumns } from '../lib/markdown.mjs';
import { checkPathCandidate, classifyPathCandidate } from '../lib/paths.mjs';
import { checkNamedReference, findNamedReferences } from '../lib/references.mjs';
import { createRepoIndex, globMatches, nearestDirectoryWith, normalizeRelative, referenceResolves } from '../lib/repo-index.mjs';
import { classifyTypeCandidate, declaredNames, typeResolves } from '../lib/symbols.mjs';
import { shippedConfig } from './helpers/tree.mjs';

const config = shippedConfig();
const index = createRepoIndex('/repo', [
  'package.json', 'docs/recipes/add-page.md', 'frontend/web/src/app/core/api.service.ts', 'tools/build.ps1',
  '.claude/skills/new-module/SKILL.md', '.claude/agents/commit-gate.md',
]);
const pathOptions = { extensions: config.pathExtensions, topLevelEntries: new Set(['docs', 'frontend', 'tools', '.claude', 'package.json']) };
const classify = (text) => classifyPathCandidate(text, pathOptions);

describe('classifyPathCandidate', () => {
  it('accepts file names, nested paths, folders and paths under a top-level folder', () => {
    assert.equal(classify('package.json').path, 'package.json');
    assert.equal(classify('tools\\build.ps1').path, 'tools/build.ps1');
    assert.equal(classify('docs/recipes/').directoryOnly, true);
    assert.equal(classify('frontend/web').path, 'frontend/web');
    assert.equal(classify('docs/recipes/*.md').glob, true);
  });

  it('rejects URLs, packages, config keys, routes, options, expressions and bare extensions', () => {
    for (const text of ['https://x/y.md', '@codelab/ng-dga', 'Codelab:Security:Tickets', '/health/ready', '--check',
      'a b.md', 'ex.Message', '.ts', '.shell__brand', 'ar/en', 'response.json()', '']) {
      assert.equal(classify(text), null, text);
    }
  });

  it('records the folders before a placeholder', () => {
    assert.equal(classify('docs/features/<slug>.md').placeholderPrefix, 'docs/features');
    assert.equal(classify('<area>/x.md').placeholderPrefix, '');
  });

  it('reads "x.props/.targets" as the first sibling', () => {
    assert.equal(classify('tools/build.ps1/.psm1').path, 'tools/build.ps1');
  });
});

describe('repo index resolution', () => {
  it('normalizes and refuses paths that leave the repository', () => {
    assert.equal(normalizeRelative('a/./b/../c/'), 'a/c');
    assert.equal(normalizeRelative('../x'), null);
  });

  it('resolves root-relative, document-relative and path-tail references, case-sensitively', () => {
    const context = { documentDirectory: 'docs/recipes' };
    assert.equal(referenceResolves(index, 'tools/build.ps1', context), true);
    assert.equal(referenceResolves(index, 'add-page.md', context), true);
    assert.equal(referenceResolves(index, 'core/api.service.ts', context), true);
    assert.equal(referenceResolves(index, 'core/api.service.ts', { ...context, allowSuffix: false }), false);
    assert.equal(referenceResolves(index, 'Tools/build.ps1', context), false);
    assert.equal(referenceResolves(index, 'docs/recipes', { ...context, directoryOnly: true }), true);
    assert.equal(referenceResolves(index, 'package.json', { ...context, directoryOnly: true }), false);
  });

  it('matches globs anchored or at any depth', () => {
    assert.equal(globMatches(index, 'docs/**/*.md', { documentDirectory: '' }), true);
    assert.equal(globMatches(index, 'app/**/*.ts', { documentDirectory: '' }), true);
    assert.equal(globMatches(index, 'docs/**/*.cs', { documentDirectory: '' }), false);
  });

  it('finds the nearest folder holding a file', () => {
    assert.equal(nearestDirectoryWith(index, 'docs/recipes', 'package.json'), '');
  });
});

describe('checkPathCandidate', () => {
  const check = (text) => checkPathCandidate(index, classify(text), { documentDirectory: 'docs/recipes' });

  it('passes existing paths and reports missing ones with the right rule', () => {
    assert.equal(check('tools/build.ps1'), null);
    assert.equal(check('tools/gone.ps1').ruleId, RuleIds.pathMissing);
    assert.equal(check('docs/**/*.yaml').ruleId, RuleIds.globEmpty);
  });

  it('checks only the folder before a placeholder', () => {
    assert.equal(check('docs/recipes/<name>.md'), null);
    assert.equal(check('docs/nowhere/<name>.md').ruleId, RuleIds.pathMissing);
    assert.equal(check('<area>/x.md'), null);
  });
});

describe('symbols', () => {
  it('indexes C# types, members and enum members, PowerShell enums and TypeScript declarations', () => {
    const csharp = declaredNames('a.cs', 'public sealed record Thing(int A);\npublic enum Gap { Missing = 1, // x\n NotInRing = 2 }\n'
      + 'public interface IPort { }\npublic IList<string> PinnedKeys { get; set; }\npublic const int Limit = 3;\n');
    for (const name of ['Thing', 'Gap', 'Missing', 'NotInRing', 'IPort', 'PinnedKeys', 'Limit']) assert.ok(csharp.has(name), name);
    const powershell = declaredNames('a.ps1', 'enum ApiGap {\n    Missing = 1\n    Empty = 2\n}\nfunction Get-Thing { }\n');
    for (const name of ['ApiGap', 'Missing', 'Empty', 'Get-Thing']) assert.ok(powershell.has(name), name);
    const typescript = declaredNames('a.ts', 'export abstract class ApiService {}\nexport type Key = string;\nexport const permissionGuard = 1;\n');
    for (const name of ['ApiService', 'Key', 'permissionGuard']) assert.ok(typescript.has(name), name);
  });

  it('treats suffixed, generic, interface, attribute and qualified names as types, and nothing else', () => {
    const types = config.types;
    assert.deepEqual(classifyTypeCandidate('ApiService', types), { display: 'ApiService', names: ['ApiService', 'ApiServiceAttribute'] });
    assert.equal(classifyTypeCandidate('PublicId<T>', types).display, 'PublicId');
    assert.equal(classifyTypeCandidate('IUserDirectory', types).display, 'IUserDirectory');
    assert.deepEqual(classifyTypeCandidate('[HasPermission]', types).names, ['HasPermission', 'HasPermissionAttribute']);
    assert.equal(classifyTypeCandidate('SlaOptions.MaxEscalations', types).display, 'SlaOptions');
    assert.equal(classifyTypeCandidate('ToJsonOptions()', types).display, 'ToJsonOptions');
    for (const text of ['[Unreleased]', 'ServiceRequests', 'GATEWAY_UPSTREAM', 'Service', 'Starter.Domain', 'apiService', 'CI']) {
      assert.equal(classifyTypeCandidate(text, types), null, text);
    }
  });

  it('resolves through the index or the external list', () => {
    const symbols = new Set(['HasPermissionAttribute']);
    assert.equal(typeResolves({ names: ['HasPermission', 'HasPermissionAttribute'] }, symbols, new Set()), true);
    assert.equal(typeResolves({ names: ['TimeProvider'] }, symbols, new Set(['TimeProvider'])), true);
    assert.equal(typeResolves({ names: ['GoneService'] }, symbols, new Set()), false);
  });
});

describe('named references', () => {
  const find = (text) => {
    const lines = readMarkdownLines(text);
    const rows = readTableColumns(lines);
    return lines.flatMap((line) => findNamedReferences(line.text, readCodeSpans(line.text), rows.get(line.number), config.references));
  };

  it('finds "the `x` skill", chains, "agent `y`" and table Skill/Agent columns', () => {
    assert.deepEqual(find('Load the `new-module` skill.').map((ref) => [ref.kind, ref.name]), [[ReferenceKinds.skill, 'new-module']]);
    assert.deepEqual(find('The `a-b`, `c` and `d` skills.').map((ref) => ref.name), ['a-b', 'c', 'd']);
    assert.deepEqual(find('Hand it to agent `commit-gate`.').map((ref) => [ref.kind, ref.name]), [[ReferenceKinds.agent, 'commit-gate']]);
    assert.deepEqual(find('| Work | Skill |\n| --- | --- |\n| area | `new-module` |\n').map((ref) => ref.name), ['new-module']);
    assert.deepEqual(find('| Work | Agent |\n| --- | --- |\n| gate | `/commit-gate` |\n').map((ref) => [ref.kind, ref.name]), [[ReferenceKinds.agent, 'commit-gate']]);
  });

  it('ignores spans that are not named as a skill or agent, and names that are not kebab-case', () => {
    assert.deepEqual(find('Run `npm test` and the `Skill` tool, then `x`.'), []);
    assert.deepEqual(find('the `postman:bootstrap` skill'), []);
  });

  it('checks existence under .claude and honours the external lists', () => {
    const references = { ...config.references, externalSkills: new Set(['simplify']) };
    assert.equal(checkNamedReference(index, { kind: ReferenceKinds.skill, name: 'new-module' }, references), null);
    assert.equal(checkNamedReference(index, { kind: ReferenceKinds.skill, name: 'simplify' }, references), null);
    assert.equal(checkNamedReference(index, { kind: ReferenceKinds.agent, name: 'commit-gate' }, references), null);
    assert.equal(checkNamedReference(index, { kind: ReferenceKinds.skill, name: 'gone' }, references).ruleId, RuleIds.skillMissing);
    assert.equal(checkNamedReference(index, { kind: ReferenceKinds.agent, name: 'gone' }, references).ruleId, RuleIds.agentMissing);
  });
});

describe('configuration files', () => {
  it('refuses a config that misses a list', () => {
    assert.throws(() => parseConfig('{"scope":{"include":[]}}', 'c.json'), /"scope.exclude" must be an array of strings/);
    assert.throws(() => parseConfig('{', 'c.json'), /invalid JSON/);
  });

  it('parses stale terms with the scrub term format and renames the ids', () => {
    const [rule] = parseStaleTerms('# c\nold-ui/i: \\bkarma\\b\n', 's.txt');
    assert.equal(rule.id, 'stale-old-ui');
    assert.equal(rule.pattern.test('Karma runner'), true);
    assert.throws(() => parseStaleTerms('bad line', 's.txt'), /s\.txt:1/);
  });

  it('parses the allowlist and requires a reason', () => {
    const [entry] = parseAllowlist('# c\npath-missing | docs/** | a.md | made at runtime | really\n', 'a.txt');
    assert.deepEqual(entry, { ruleId: 'path-missing', fileGlob: 'docs/**', subject: 'a.md', reason: 'made at runtime | really', line: 2 });
    assert.throws(() => parseAllowlist('path-missing | docs/** | a.md\n', 'a.txt'), /a\.txt:1/);
    assert.throws(() => parseAllowlist('path-missing | docs/** | a.md | \n', 'a.txt'), /a\.txt:1/);
  });

  it('selects rule ids by exact id or prefix and subjects by exact text or wildcard', () => {
    assert.equal(ruleIdSelected('stale-*', 'stale-karma'), true);
    assert.equal(ruleIdSelected('path-missing', 'glob-empty'), false);
    assert.equal(subjectSelected('*', 'x'), true);
    assert.equal(subjectSelected('*local.json', 'a/b.local.json'), true);
    assert.equal(subjectSelected('a.(json)', 'a.(json)'), true);
    assert.equal(subjectSelected('a.json', 'a.jsonx'), false);
  });
});
