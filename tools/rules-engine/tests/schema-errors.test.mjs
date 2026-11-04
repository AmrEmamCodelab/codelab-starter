import { test, describe, beforeEach, afterEach } from 'node:test';
import assert from 'node:assert/strict';
import { compileRepository } from '../lib/engine.mjs';
import { RulesError } from '../lib/errors.mjs';
import { PATHS } from '../lib/constants.mjs';
import { createWorkspace, removeWorkspace, writeWorkspaceFile, moduleYaml, readWorkspaceFile } from './helpers/workspace.mjs';

const MODULE_FILE = 'src/Modules/Records/module.rules.yaml';

/** Compiles and returns the problem lines; fails the test when compilation succeeds. */
function problemsOf(root) {
  try {
    compileRepository(root);
  } catch (error) {
    assert.ok(error instanceof RulesError, `expected RulesError, got ${error}`);
    return error.problems;
  }
  assert.fail('expected the rules to be rejected');
}

function assertProblem(problems, file, fragment) {
  assert.ok(
    problems.some((problem) => problem.startsWith(`${file}:`) && problem.includes(fragment)),
    `expected a problem in ${file} containing "${fragment}", got:\n${problems.join('\n')}`,
  );
}

const forbiddenRule = (id, pattern) => [
  'forbidden:',
  `  - id: ${id}`,
  '    description: A forbidden thing for the test.',
  `    pattern: '${pattern}'`,
  '    appliesTo: ["**/*.cs"]',
  '    fix: Do the allowed thing instead.',
  '',
].join('\n');

describe('rules validation errors', () => {
  let root;
  beforeEach(() => {
    root = createWorkspace();
  });
  afterEach(() => removeWorkspace(root));

  test('regression: a regex with nested unbounded quantifiers (ReDoS) is rejected', () => {
    writeWorkspaceFile(root, MODULE_FILE, moduleYaml('Records', forbiddenRule('records.slow', '^(\\w+\\s?)*$')));
    assertProblem(problemsOf(root), MODULE_FILE, 'catastrophic backtracking');
  });

  test('regression: a recipe that does not exist is rejected (dead reference in every module CLAUDE.md)', () => {
    writeWorkspaceFile(root, MODULE_FILE, moduleYaml('Records', 'recipes:\n  - docs/standards/missing.md\n'));
    assertProblem(problemsOf(root), MODULE_FILE, "recipe 'docs/standards/missing.md' does not exist");
  });

  test('unknown property is a schema error naming the property', () => {
    writeWorkspaceFile(root, MODULE_FILE, moduleYaml('Records', 'mayDependsOn: [Workflow]\n'));
    assertProblem(problemsOf(root), MODULE_FILE, "'mayDependsOn'");
  });

  test('missing module name is a schema error', () => {
    writeWorkspaceFile(root, MODULE_FILE, 'schemaVersion: 1\n');
    assertProblem(problemsOf(root), MODULE_FILE, "must have required property 'module'");
  });

  test('wrong schemaVersion is a schema error', () => {
    writeWorkspaceFile(root, MODULE_FILE, 'schemaVersion: 2\nmodule: Records\n');
    assertProblem(problemsOf(root), MODULE_FILE, '/schemaVersion');
  });

  test('invalid YAML reports the parser error', () => {
    writeWorkspaceFile(root, MODULE_FILE, 'schemaVersion: 1\nmodule: [Records\n');
    assertProblem(problemsOf(root), MODULE_FILE, 'YAML');
  });

  test('duplicate YAML keys are rejected', () => {
    writeWorkspaceFile(root, MODULE_FILE, 'schemaVersion: 1\nmodule: Records\nmodule: Other\n');
    assertProblem(problemsOf(root), MODULE_FILE, 'YAML');
  });

  test('a regex that does not compile is reported with the rule id', () => {
    writeWorkspaceFile(root, MODULE_FILE, moduleYaml('Records', forbiddenRule('records.broken', '(unclosed')));
    assertProblem(problemsOf(root), MODULE_FILE, "forbidden rule 'records.broken' has an invalid pattern");
  });

  test('module rule ids must start with the lower-cased module name', () => {
    writeWorkspaceFile(root, MODULE_FILE, moduleYaml('Records', forbiddenRule('other.rule', 'x')));
    assertProblem(problemsOf(root), MODULE_FILE, "must start with 'records.'");
  });

  test('a module cannot redeclare a base rule id', () => {
    writeWorkspaceFile(root, MODULE_FILE, moduleYaml('Records', forbiddenRule('base.time-provider', 'x')));
    assertProblem(problemsOf(root), MODULE_FILE, "must start with 'records.'");
  });

  test('exempting an unknown base rule is rejected', () => {
    writeWorkspaceFile(root, MODULE_FILE, moduleYaml('Records', 'exemptions:\n  - rule: base.does-not-exist\n    reason: Because the test says so.\n'));
    assertProblem(problemsOf(root), MODULE_FILE, "exemption 'base.does-not-exist' does not name a base rule");
  });

  test('an exemption without a real reason is a schema error', () => {
    writeWorkspaceFile(root, MODULE_FILE, moduleYaml('Records', 'exemptions:\n  - rule: base.time-provider\n    reason: no\n'));
    assertProblem(problemsOf(root), MODULE_FILE, '/exemptions/0/reason');
  });

  test('a module both allowed and forbidden as a dependency is rejected', () => {
    writeWorkspaceFile(root, MODULE_FILE, moduleYaml('Records', 'mayDependOn: [Workflow]\nmustNotDependOn: [Workflow]\n'));
    assertProblem(problemsOf(root), MODULE_FILE, "'Workflow' is in both mayDependOn and mustNotDependOn");
  });

  test('a module depending on itself is rejected', () => {
    writeWorkspaceFile(root, MODULE_FILE, moduleYaml('Records', 'mayDependOn: [Records]\n'));
    assertProblem(problemsOf(root), MODULE_FILE, 'cannot list itself');
  });

  test('two modules with the same name are rejected', () => {
    writeWorkspaceFile(root, MODULE_FILE, moduleYaml('Records'));
    writeWorkspaceFile(root, 'src/Other/module.rules.yaml', moduleYaml('Records'));
    assertProblem(problemsOf(root), 'src/Other/module.rules.yaml', "module name 'Records' is already used");
  });

  test('kind dotnet without a .csproj is rejected', () => {
    writeWorkspaceFile(root, MODULE_FILE, moduleYaml('Records', 'kind: dotnet\n'));
    assertProblem(problemsOf(root), MODULE_FILE, 'has no .csproj');
  });

  test('a banned symbol that is not a documentation id is a schema error', () => {
    writeWorkspaceFile(root, MODULE_FILE, moduleYaml('Records', 'bannedSymbols:\n  - id: records.x\n    symbol: System.Console\n    fix: Use ILogger instead of the console.\n'));
    assertProblem(problemsOf(root), MODULE_FILE, '/bannedSymbols/0/symbol');
  });

  test('a banned symbol already in the Codelab.Build.Sdk global list is rejected', () => {
    writeWorkspaceFile(root, PATHS.sdkGlobalBannedSymbols, '// comment\nP:System.DateTime.Now;Use TimeProvider.\n');
    writeWorkspaceFile(root, MODULE_FILE, moduleYaml('Records', 'bannedSymbols:\n  - id: records.now\n    symbol: P:System.DateTime.Now\n    fix: Inject TimeProvider instead.\n'));
    assertProblem(problemsOf(root), MODULE_FILE, 'already banned globally by Codelab.Build.Sdk');
  });

  test('a module repeating a base banned symbol is rejected', () => {
    writeWorkspaceFile(root, MODULE_FILE, moduleYaml('Records', 'bannedSymbols:\n  - id: records.sleep\n    symbol: M:System.Threading.Thread.Sleep(System.Int32)\n    fix: Use Task.Delay with TimeProvider.\n'));
    assertProblem(problemsOf(root), MODULE_FILE, 'is listed more than once');
  });

  test('threshold out of range is a schema error', () => {
    writeWorkspaceFile(root, MODULE_FILE, moduleYaml('Records', 'thresholds:\n  cyclomaticComplexity: 500\n'));
    assertProblem(problemsOf(root), MODULE_FILE, '/thresholds/cyclomaticComplexity');
  });

  test('every problem in every file is reported in one run', () => {
    writeWorkspaceFile(root, MODULE_FILE, moduleYaml('Records', 'mayDependsOn: [X]\n'));
    writeWorkspaceFile(root, 'src/Other/module.rules.yaml', moduleYaml('Other', forbiddenRule('wrong.prefix', 'x')));
    const problems = problemsOf(root);
    assertProblem(problems, MODULE_FILE, "'mayDependsOn'");
    assertProblem(problems, 'src/Other/module.rules.yaml', "must start with 'other.'");
  });

  test('an invalid base file stops compilation with the base file named', () => {
    const base = readWorkspaceFile(root, PATHS.baseRules).replace('ruleSet: base', 'ruleSet: core');
    writeWorkspaceFile(root, PATHS.baseRules, base);
    assertProblem(problemsOf(root), PATHS.baseRules, '/ruleSet');
  });
});
