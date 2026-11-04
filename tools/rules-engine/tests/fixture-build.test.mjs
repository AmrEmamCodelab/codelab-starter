// Phase 0 exit criterion: "a fixture module's module.rules.yaml produces a BannedSymbols.generated.txt, and a banned
// call in that project fails the build". Uses the real .NET 10 SDK and the real Codelab.Build.Sdk (from source).
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
const VIOLATION_FILE = `${MODULE_DIR}/Violation.cs`;
const BUILD_TEST_TIMEOUT_MS = 10 * 60 * 1000;

const VIOLATION_SOURCE = `namespace Starter.Modules.Sample;

/// <summary>Deliberate rule violations for the rules-engine build test.</summary>
public static class Violation
{
    /// <summary>Uses one inherited (base) and one module-specific banned API.</summary>
    public static Workflow.SampleStatus Wait(Workflow.SampleStatus target)
    {
        Thread.Sleep(millisecondsTimeout: 5);
        return new Workflow.LegacyWorkflowRunner().Run(target);
    }
}
`;

describe('SampleModule fixture: rules -> BannedSymbols.generated.txt -> failing build', { timeout: BUILD_TEST_TIMEOUT_MS }, () => {
  let root;
  let projectPath;
  before(() => {
    root = createWorkspace();
    copyFixtureModule(root, MODULE_DIR);
    wireWorkspaceToRepoBuild(root);
    projectPath = path.join(root, ...PROJECT_FILE.split('/'));
  });
  after(() => removeWorkspace(root));

  test('the engine writes BannedSymbols.generated.txt next to the project', () => {
    assert.ok(!existsSync(path.join(root, ...BANNED_FILE.split('/'))));
    writeOutputs(root, compileRepository(root).outputs);
    const banned = readWorkspaceFile(root, BANNED_FILE);
    assert.match(banned, /^M:Starter\.Modules\.Sample\.Workflow\.LegacyWorkflowRunner\.#ctor;.*\(rule sample\.no-legacy-runner\)$/m);
    assert.match(banned, /^M:System\.Threading\.Thread\.Sleep\(System\.Int32\);.*\(rule base\.no-thread-sleep\)$/m);
  });

  test('the clean fixture builds with -warnaserror', () => {
    const result = dotnetBuild(projectPath);
    assert.equal(result.status, 0, result.output);
  });

  test('a banned call fails the build with RS0030 and the rule id + fix message', () => {
    writeWorkspaceFile(root, VIOLATION_FILE, VIOLATION_SOURCE);
    const result = dotnetBuild(projectPath);
    assert.notEqual(result.status, 0, 'the build must fail');
    assert.match(result.output, /Violation\.cs\(\d+,\d+\): error RS0030: .*LegacyWorkflowRunner.*Inject IWorkflowEngine.*\(rule sample\.no-legacy-runner\)/);
    assert.match(result.output, /Violation\.cs\(\d+,\d+\): error RS0030: .*Thread\.Sleep.*\(rule base\.no-thread-sleep\)/);
  });

  test('without the generated file the same code builds (the ban comes from the rules)', () => {
    rmSync(path.join(root, ...BANNED_FILE.split('/')));
    const result = dotnetBuild(projectPath);
    assert.equal(result.status, 0, result.output);
  });
});
