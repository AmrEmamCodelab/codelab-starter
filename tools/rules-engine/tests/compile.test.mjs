import { test, describe, beforeEach, afterEach } from 'node:test';
import assert from 'node:assert/strict';
import { compileRepository } from '../lib/engine.mjs';
import { PATHS } from '../lib/constants.mjs';
import { createWorkspace, removeWorkspace, writeWorkspaceFile, moduleYaml, copyFixtureModule } from './helpers/workspace.mjs';

const RECORDS_RULES = 'src/Modules/Records/module.rules.yaml';

describe('compileRepository', () => {
  let root;
  beforeEach(() => {
    root = createWorkspace();
  });
  afterEach(() => removeWorkspace(root));

  test('base only: compiled base, no modules, compiled JSON + ESLint data emitted', () => {
    const { compiled, outputs } = compileRepository(root);
    assert.equal(compiled.modules.length, 0);
    assert.ok(compiled.base.forbidden.some((rule) => rule.id === 'base.time-provider'));
    assert.deepEqual(outputs.map((output) => output.path), [PATHS.compiled, PATHS.eslintBoundaries]);
    assert.deepEqual(JSON.parse(outputs[0].content), compiled);
  });

  test('a module inherits every base rule and adds its own after them', () => {
    writeWorkspaceFile(root, RECORDS_RULES, moduleYaml('Records', [
      'forbidden:',
      '  - id: records.no-direct-sql',
      '    description: Raw SQL bypasses the repository.',
      "    pattern: '\\bFromSqlRaw\\b'",
      '    appliesTo: ["**/*.cs"]',
      '    fix: Add a repository method on the Records port.',
      '',
    ].join('\n')));
    const { compiled } = compileRepository(root);
    const [module] = compiled.modules;
    const baseIds = compiled.base.forbidden.map((rule) => rule.id);
    assert.deepEqual(module.forbidden.slice(0, baseIds.length).map((rule) => rule.id), baseIds);
    const own = module.forbidden.at(-1);
    assert.equal(own.id, 'records.no-direct-sql');
    assert.equal(own.source, 'module');
    assert.equal(own.severity, 'error', 'severity defaults to error');
    assert.deepEqual(own.appliesTo, ['src/Modules/Records/**/*.cs'], 'module globs become repo-relative');
    assert.deepEqual(module.excludes, undefined);
  });

  test('thresholds and analyzers override per key; owners default to base owners', () => {
    writeWorkspaceFile(root, RECORDS_RULES, moduleYaml('Records', 'thresholds:\n  methodLines: 25\nanalyzers:\n  CA1502: error\n'));
    const { compiled } = compileRepository(root);
    const [module] = compiled.modules;
    assert.equal(module.thresholds.methodLines, 25);
    assert.equal(module.thresholds.fileLines, compiled.base.thresholds.fileLines);
    assert.equal(module.analyzers.CA1502, 'error');
    assert.equal(module.analyzers.RS0030, compiled.base.analyzers.RS0030);
    assert.deepEqual(module.owners, compiled.base.owners);
  });

  test('an exemption removes exactly that base rule and is recorded with its reason', () => {
    writeWorkspaceFile(root, RECORDS_RULES, moduleYaml('Records', [
      'exemptions:',
      '  - rule: base.time-provider',
      '    reason: This module wraps the clock for the kernel.',
      '',
    ].join('\n')));
    const { compiled } = compileRepository(root);
    const [module] = compiled.modules;
    assert.ok(!module.forbidden.some((rule) => rule.id === 'base.time-provider'));
    assert.equal(module.forbidden.length, compiled.base.forbidden.length - 1);
    assert.deepEqual(module.exemptions, [{ rule: 'base.time-provider', reason: 'This module wraps the clock for the kernel.' }]);
  });

  test('kind is inferred: a folder with a .csproj is dotnet and gets the .NET emitters, otherwise angular', () => {
    writeWorkspaceFile(root, RECORDS_RULES, moduleYaml('Records'));
    writeWorkspaceFile(root, 'src/Modules/Records/Starter.Modules.Records.csproj', '<Project Sdk="Microsoft.NET.Sdk" />\n');
    writeWorkspaceFile(root, 'web/features/records/module.rules.yaml', moduleYaml('RecordsUi'));
    const { compiled, outputs } = compileRepository(root);
    const kinds = Object.fromEntries(compiled.modules.map((module) => [module.module, module.kind]));
    assert.deepEqual(kinds, { Records: 'dotnet', RecordsUi: 'angular' });
    const paths = outputs.map((output) => output.path);
    assert.ok(paths.includes('src/Modules/Records/BannedSymbols.generated.txt'));
    assert.ok(paths.includes('src/Modules/Records/.editorconfig'));
    assert.ok(paths.includes('src/Modules/Records/CodeMetricsConfig.txt'));
    assert.ok(paths.includes('web/features/records/CLAUDE.md'));
    assert.ok(!paths.includes('web/features/records/BannedSymbols.generated.txt'));
  });

  test('modules are ordered by path and folders named fixtures/node_modules/bin/obj are skipped', () => {
    writeWorkspaceFile(root, 'src/b/module.rules.yaml', moduleYaml('Bravo'));
    writeWorkspaceFile(root, 'src/a/module.rules.yaml', moduleYaml('Alpha'));
    writeWorkspaceFile(root, 'tools/x/fixtures/m/module.rules.yaml', moduleYaml('Ignored'));
    writeWorkspaceFile(root, 'node_modules/p/module.rules.yaml', moduleYaml('IgnoredToo'));
    writeWorkspaceFile(root, 'src/a/bin/module.rules.yaml', moduleYaml('IgnoredBin'));
    const { compiled } = compileRepository(root);
    assert.deepEqual(compiled.modules.map((module) => module.module), ['Alpha', 'Bravo']);
  });

  test('the SampleModule fixture compiles with its module rules merged on top of base', () => {
    copyFixtureModule(root, 'src/Modules/Sample');
    const { compiled } = compileRepository(root);
    const [module] = compiled.modules;
    assert.equal(module.module, 'Sample');
    assert.equal(module.kind, 'dotnet');
    assert.deepEqual(module.mayDependOn, ['Records', 'Workflow']);
    assert.ok(module.bannedSymbols.some((entry) => entry.id === 'sample.no-legacy-runner'));
    assert.ok(module.bannedSymbols.some((entry) => entry.id === 'base.no-thread-sleep'));
    assert.ok(!module.forbidden.some((rule) => rule.id === 'base.fe-raw-text-in-template'));
    assert.equal(module.thresholds.methodLines, 30);
  });

  test('compiling twice gives byte-identical outputs (deterministic)', () => {
    copyFixtureModule(root, 'src/Modules/Sample');
    assert.deepEqual(compileRepository(root).outputs, compileRepository(root).outputs);
  });
});
