import { test, describe, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { compileRepository } from '../lib/engine.mjs';
import { emitBannedSymbols } from '../lib/emitters/banned-symbols.mjs';
import { emitEditorconfig } from '../lib/emitters/editorconfig.mjs';
import { emitCodeMetrics } from '../lib/emitters/code-metrics.mjs';
import { emitClaudeMd, codeSpan } from '../lib/emitters/claude-md.mjs';
import { emitEslintBoundaries } from '../lib/emitters/eslint-boundaries.mjs';
import { GENERATED_MARKER } from '../lib/constants.mjs';
import { createWorkspace, removeWorkspace, copyFixtureModule } from './helpers/workspace.mjs';

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

  test('BannedSymbols.generated.txt: comment header, then <doc id>;<fix> (rule <id>) for base + module symbols', () => {
    const lines = emitBannedSymbols(sample).trimEnd().split('\n');
    const comments = lines.filter((line) => line.startsWith('//'));
    const entries = lines.filter((line) => !line.startsWith('//'));
    assert.ok(comments[0].includes(GENERATED_MARKER));
    assert.deepEqual(entries, [
      'M:System.Threading.Thread.Sleep(System.Int32);Use await Task.Delay(delay, timeProvider, cancellationToken) so waits are async and testable. (rule base.no-thread-sleep)',
      'M:System.Threading.Thread.Sleep(System.TimeSpan);Use await Task.Delay(delay, timeProvider, cancellationToken) so waits are async and testable. (rule base.no-thread-sleep-timespan)',
      'M:Starter.Modules.Sample.Workflow.LegacyWorkflowRunner.#ctor;Inject IWorkflowEngine instead of constructing LegacyWorkflowRunner. (rule sample.no-legacy-runner)',
    ]);
  });

  test('.editorconfig: non-root, one [*.cs] section, severities sorted by id, module override included', () => {
    const text = emitEditorconfig(sample);
    assert.ok(!/^root\s*=/m.test(text), 'a module .editorconfig must never be root');
    const section = text.slice(text.indexOf('[*.cs]'));
    assert.equal(section, [
      '[*.cs]',
      'dotnet_diagnostic.CA1062.severity = warning',
      'dotnet_diagnostic.CA1502.severity = warning',
      'dotnet_diagnostic.RS0030.severity = error',
      '',
    ].join('\n'));
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
