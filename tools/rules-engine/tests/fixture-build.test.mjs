// Phase 0 exit criterion: "a fixture module's module.rules.yaml produces a BannedSymbols.generated.txt, and a banned
// call in that project fails the build". Phase 1 (ADR-012): the module's thresholds reach Codelab.Build.Analyzers
// through the generated .editorconfig and override the repo-wide rules/codelab.globalconfig. Uses the real .NET 10 SDK
// and the real Codelab.Build.Sdk (from source, with the analyzer project referenced as an Analyzer).
import { test, describe, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, rmSync } from 'node:fs';
import path from 'node:path';
import { compileRepository, writeOutputs } from '../lib/engine.mjs';
import { createWorkspace, removeWorkspace, copyFixtureModule, writeWorkspaceFile, readWorkspaceFile } from './helpers/workspace.mjs';
import { dotnetBuild, wireWorkspaceToRepoBuild } from './helpers/dotnet.mjs';

const MODULE_DIR = 'src/Modules/Sample';
const PROJECT_FILE = `${MODULE_DIR}/Starter.Modules.Sample.csproj`;
const BANNED_FILE = `${MODULE_DIR}/BannedSymbols.generated.txt`;
const EDITORCONFIG_FILE = `${MODULE_DIR}/.editorconfig`;
const VIOLATION_FILE = `${MODULE_DIR}/Violation.cs`;
const BUILD_TEST_TIMEOUT_MS = 10 * 60 * 1000;
// The fixture's module.rules.yaml sets thresholds.methodLines: 30; the base (and rules/codelab.globalconfig) says 40.
const MODULE_METHOD_LINES = 30;

const MODULE_BAN_SOURCE = `namespace Starter.Modules.Sample;

/// <summary>Deliberate module rule violation for the rules-engine build test.</summary>
public static class Violation
{
    /// <summary>Uses the module-specific banned constructor.</summary>
    public static Workflow.SampleStatus Run(Workflow.SampleStatus target) => new Workflow.LegacyWorkflowRunner().Run(target);
}
`;

const GLOBAL_BAN_SOURCE = `namespace Starter.Modules.Sample;

/// <summary>Deliberate global ban violation: Thread.Sleep is in Codelab.Build.Sdk's BannedSymbols.txt.</summary>
public static class Violation
{
    /// <summary>Blocks the thread.</summary>
    public static void Wait() => Thread.Sleep(millisecondsTimeout: 0);
}
`;

/** A class whose method body has exactly `bodyLines` code lines (braces included). */
function longMethodSource(bodyLines) {
  const statements = Array.from({ length: bodyLines - 3 }, () => '        total += value;');
  return [
    'namespace Starter.Modules.Sample;',
    '',
    '/// <summary>Deliberately long method for the threshold test.</summary>',
    'public static class Violation',
    '{',
    '    /// <summary>Adds the value many times.</summary>',
    '    public static int Sum(int value)',
    '    {',
    ...statements,
    '        return total;',
    '    }',
    '}',
    '',
  ].join('\n').replace('        total += value;', '        var total = value;');
}

describe('SampleModule fixture: rules -> generated files -> failing build', { timeout: BUILD_TEST_TIMEOUT_MS }, () => {
  let root;
  let projectPath;
  before(() => {
    root = createWorkspace();
    copyFixtureModule(root, MODULE_DIR);
    wireWorkspaceToRepoBuild(root);
    projectPath = path.join(root, ...PROJECT_FILE.split('/'));
  });
  after(() => removeWorkspace(root));

  test('the engine writes BannedSymbols.generated.txt and the .editorconfig with the module thresholds', () => {
    assert.ok(!existsSync(path.join(root, ...BANNED_FILE.split('/'))));
    writeOutputs(root, compileRepository(root).outputs);
    const banned = readWorkspaceFile(root, BANNED_FILE);
    assert.match(banned, /^M:Starter\.Modules\.Sample\.Workflow\.LegacyWorkflowRunner\.#ctor;.*\(rule sample\.no-legacy-runner\)$/m);
    assert.match(readWorkspaceFile(root, EDITORCONFIG_FILE), new RegExp(`^codelab_method_lines = ${MODULE_METHOD_LINES}$`, 'm'));
  });

  test('the clean fixture builds with -warnaserror', () => {
    const result = dotnetBuild(projectPath);
    assert.equal(result.status, 0, result.output);
  });

  test('a module-banned call fails the build with RS0030 and the rule id + fix message', () => {
    writeWorkspaceFile(root, VIOLATION_FILE, MODULE_BAN_SOURCE);
    const result = dotnetBuild(projectPath);
    assert.notEqual(result.status, 0, 'the build must fail');
    assert.match(result.output, /Violation\.cs\(\d+,\d+\): error RS0030: .*LegacyWorkflowRunner.*Inject IWorkflowEngine.*\(rule sample\.no-legacy-runner\)/);
  });

  test('Thread.Sleep fails the build through the global SDK list (rule base.no-thread-sleep)', () => {
    writeWorkspaceFile(root, VIOLATION_FILE, GLOBAL_BAN_SOURCE);
    const result = dotnetBuild(projectPath);
    assert.notEqual(result.status, 0, 'the build must fail');
    assert.match(result.output, /Violation\.cs\(\d+,\d+\): error RS0030: .*Thread\.Sleep.*\(rule base\.no-thread-sleep\)/);
  });

  test('the module methodLines override beats the repo-wide base value (CLB1001 at 31 lines, limit 30)', () => {
    writeWorkspaceFile(root, VIOLATION_FILE, longMethodSource(MODULE_METHOD_LINES));
    assert.equal(dotnetBuild(projectPath).status, 0, 'a body at the module limit builds');
    writeWorkspaceFile(root, VIOLATION_FILE, longMethodSource(MODULE_METHOD_LINES + 1));
    const result = dotnetBuild(projectPath);
    assert.notEqual(result.status, 0, 'the build must fail');
    assert.match(result.output, /Violation\.cs\(\d+,\d+\): error CLB1001: 'Sum' has 31 lines of code; the limit is 30 /);
  });

  test('without the generated file the module-banned call builds (the ban comes from the rules)', () => {
    writeWorkspaceFile(root, VIOLATION_FILE, MODULE_BAN_SOURCE);
    rmSync(path.join(root, ...BANNED_FILE.split('/')));
    const result = dotnetBuild(projectPath);
    assert.equal(result.status, 0, result.output);
  });
});
