import { test, describe, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { compileRepository } from '../lib/engine.mjs';
import { emitBannedSymbols } from '../lib/emitters/banned-symbols.mjs';
import { emitEditorconfig } from '../lib/emitters/editorconfig.mjs';
import { emitCodeMetrics } from '../lib/emitters/code-metrics.mjs';
import { emitClaudeMd, codeSpan } from '../lib/emitters/claude-md.mjs';
import { emitEslintBoundaries } from '../lib/emitters/eslint-boundaries.mjs';
import { emitGlobalconfig } from '../lib/emitters/globalconfig.mjs';
import { ANALYZER_SETTING_KEYS, analyzerSettings } from '../lib/emitters/analyzer-settings.mjs';
import { GENERATED_MARKER, MAGIC_NUMBERS_RULE } from '../lib/constants.mjs';
import { createWorkspace, removeWorkspace, copyFixtureModule, REPO_ROOT } from './helpers/workspace.mjs';
import { readFileSync } from 'node:fs';
import path from 'node:path';

const ANALYZER_SOURCE = path.join('packages', 'dotnet', 'Codelab.Build.Analyzers');

describe('emitters (SampleModule fixture)', () => {
  let root;
  let compiled;
  let sample;
  before(() => {
    root = createWorkspace();
    copyFixtureModule(root, 'src/Modules/Sample');
    compiled = compileRepository(root).compiled;
    [sample] = compiled.modules;
  });
  after(() => removeWorkspace(root));

  test('BannedSymbols.generated.txt: comment header, then <doc id>;<fix> (rule <id>) for module symbols', () => {
    const lines = emitBannedSymbols(sample).trimEnd().split('\n');
    const comments = lines.filter((line) => line.startsWith('//'));
    const entries = lines.filter((line) => !line.startsWith('//'));
    assert.ok(comments[0].includes(GENERATED_MARKER));
    assert.deepEqual(entries, [
      'M:Starter.Modules.Sample.Workflow.LegacyWorkflowRunner.#ctor;Inject IWorkflowEngine instead of constructing LegacyWorkflowRunner. (rule sample.no-legacy-runner)',
    ]);
  });

  test('.editorconfig: non-root, one [*.cs] section, severities sorted by id, then the codelab_* thresholds', () => {
    const text = emitEditorconfig(sample);
    assert.ok(!/^root\s*=/m.test(text), 'a module .editorconfig must never be root');
    const section = text.slice(text.indexOf('[*.cs]'));
    assert.equal(section, [
      '[*.cs]',
      'dotnet_diagnostic.CA1062.severity = warning',
      'dotnet_diagnostic.CA1502.severity = warning',
      'dotnet_diagnostic.CLB1001.severity = error',
      'dotnet_diagnostic.CLB1002.severity = error',
      'dotnet_diagnostic.CLB1003.severity = error',
      'dotnet_diagnostic.CLB1004.severity = error',
      'dotnet_diagnostic.CLB1005.severity = error',
      'dotnet_diagnostic.RS0030.severity = error',
      '',
      '# Codelab.Build.Analyzers thresholds (CLB1001-CLB1005, ADR-012)',
      'codelab_method_lines = 30',
      'codelab_file_lines = 400',
      'codelab_parameters = 5',
      'codelab_allowed_numbers = -1,0,1',
      'codelab_exclude_tests = true',
      'codelab_magic_numbers_enforced = true',
      '',
    ].join('\n'));
  });

  test('analyzer settings: a module exempt from base.no-magic-values only switches the magic-number check off', () => {
    const exempt = { ...sample, required: sample.required.filter((rule) => rule.id !== MAGIC_NUMBERS_RULE) };
    assert.deepEqual(analyzerSettings(exempt), [
      ['codelab_method_lines', '30'],
      ['codelab_file_lines', '400'],
      ['codelab_parameters', '5'],
      ['codelab_magic_numbers_enforced', 'false'],
    ]);
  });

  test('rules/codelab.globalconfig: global analyzer config with the base thresholds (no module overrides)', () => {
    const text = emitGlobalconfig(compiled.base);
    assert.ok(text.startsWith(`# ${GENERATED_MARKER}`));
    assert.match(text, /^is_global = true$/m);
    assert.ok(!/^\[/m.test(text), 'a global config carries global options only, no sections');
    assert.match(text, /^codelab_method_lines = 40$/m);
    assert.match(text, /^codelab_file_lines = 400$/m);
    assert.match(text, /^codelab_parameters = 5$/m);
    assert.match(text, /^codelab_allowed_numbers = -1,0,1$/m);
    assert.match(text, /^codelab_exclude_tests = true$/m);
    assert.match(text, /^codelab_magic_numbers_enforced = true$/m);
  });

  test('the analyzer keys match Codelab.Build.Analyzers OptionKeys.cs and its defaults match the base thresholds', () => {
    const optionKeys = readFileSync(path.join(REPO_ROOT, ANALYZER_SOURCE, 'Options', 'OptionKeys.cs'), 'utf8');
    for (const key of Object.values(ANALYZER_SETTING_KEYS)) {
      assert.match(optionKeys, new RegExp(`= "${key}";`), `OptionKeys.cs lacks ${key}`);
    }
    const defaults = readFileSync(path.join(REPO_ROOT, ANALYZER_SOURCE, 'Options', 'DefaultThresholds.cs'), 'utf8');
    const { thresholds } = compiled.base;
    assert.match(defaults, new RegExp(`MethodLines = ${thresholds.methodLines};`));
    assert.match(defaults, new RegExp(`FileLines = ${thresholds.fileLines};`));
    assert.match(defaults, new RegExp(`Parameters = ${thresholds.parameters};`));
  });

  test('CodeMetricsConfig.txt carries the cyclomatic-complexity threshold for CA1502', () => {
    assert.match(emitCodeMetrics(sample), /^CA1502: 10$/m);
  });

  test('CLAUDE.md lists dependencies, inherited and own rules with fixes, banned APIs, thresholds and exemptions', () => {
    const text = emitClaudeMd(sample);
    assert.ok(text.startsWith(`<!-- ${GENERATED_MARKER}`));
    assert.match(text, /^# Sample module: rules for AI edits$/m);
    assert.match(text, /\[MODULE\.md\]\(MODULE\.md\)/);
    assert.match(text, /May depend on: `Records`, `Workflow`/);
    assert.match(text, /Must not depend on: `Notifications`/);
    assert.match(text, /`base\.time-provider` \(error\) in `\*\*\/\*\.cs`: .* Fix: Inject TimeProvider/);
    assert.match(text, /`sample\.no-direct-runner` \(error\) in `src\/Modules\/Sample\/\*\*\/\*\.cs`/);
    assert.match(text, /`M:Starter\.Modules\.Sample\.Workflow\.LegacyWorkflowRunner\.#ctor`: Inject IWorkflowEngine/);
    assert.match(text, /^- methodLines: 30$/m);
    assert.match(text, /`base\.fe-raw-text-in-template`: Backend-only module/);
    assert.ok(!text.includes('`base.fe-raw-text-in-template` (warning)'), 'exempted rule must not be listed as forbidden');
  });

  test('codeSpan survives backticks inside the text', () => {
    assert.equal(codeSpan('a'), '`a`');
    assert.equal(codeSpan("['\"`]/api/"), "``['\"`]/api/``");
    assert.equal(codeSpan('`x'), '`` `x ``');
  });

  test('ESLint boundary data: one entry per module with its dependency lists, plus magic-number allowances', () => {
    const data = JSON.parse(emitEslintBoundaries(compiled));
    assert.deepEqual(data.noMagicNumbers, { ignore: [-1, 0, 1] });
    assert.deepEqual(data.modules, [{
      module: 'Sample',
      kind: 'dotnet',
      path: 'src/Modules/Sample',
      mayDependOn: ['Records', 'Workflow'],
      mustNotDependOn: ['Notifications'],
    }]);
  });
});
