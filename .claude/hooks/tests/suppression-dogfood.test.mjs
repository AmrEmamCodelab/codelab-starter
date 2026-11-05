// Dogfood: the REAL rules/rules.compiled.json refuses analyzer bypass forms through pre-tool-use.mjs
// (a spaced pragma, an aliased SuppressMessage, an analyzer config redirect and a ruleset file).
import { after, before, describe, it } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {
  HOOKS_DIR,
  createFixtureProject,
  decisionOf,
  reasonOf,
  removeProject,
  runHookScript,
  toolInput,
  writeFiles,
} from './helpers/fixture-project.mjs';

const SCRIPT = 'pre-tool-use.mjs';
const DENY = 'deny';
const REPO_COMPILED_RULES = path.resolve(HOOKS_DIR, '..', '..', 'rules', 'rules.compiled.json');
const MODULE_DIR = 'backend/src/Modules/Demo';
const MODULE_FILES = Object.freeze({
  [`${MODULE_DIR}/module.rules.yaml`]: 'module: Demo\n',
  [`${MODULE_DIR}/MODULE.md`]: '# Demo\n\n## Purpose\nFixture.\n\n## Use\n-\n\n## Extend\n-\n\n## Rules\n-\n\n## Gotchas\n-\n',
});

const write = (root, relativePath, content) =>
  toolInput({ projectRoot: root, toolName: 'Write', toolInputFields: { file_path: `${root}/${relativePath}`, content } });

const CASES = Object.freeze([
  {
    name: 'an aliased SuppressMessageAttribute',
    file: `${MODULE_DIR}/Application/B.cs`,
    content: 'using SM = System.Diagnostics.CodeAnalysis.SuppressMessageAttribute;\nsealed class B { }\n',
    rule: /\[base\.no-inline-suppression\]/,
  },
  {
    name: 'a pragma with a space after the hash',
    file: `${MODULE_DIR}/Application/C.cs`,
    content: '# pragma warning disable CA2000\nsealed class C { }\n',
    rule: /\[base\.no-inline-suppression\]/,
  },
  {
    name: 'a CodeAnalysisRuleSet in a module csproj',
    file: `${MODULE_DIR}/Starter.Modules.Demo.csproj`,
    content: '<Project><PropertyGroup><CodeAnalysisRuleSet>off.ruleset</CodeAnalysisRuleSet></PropertyGroup></Project>\n',
    rule: /\[base\.no-analyzer-config-redirect\]/,
  },
  {
    name: 'a GlobalAnalyzerConfigFiles item in a module csproj',
    file: `${MODULE_DIR}/Starter.Modules.Demo.csproj`,
    content: '<Project><ItemGroup><GlobalAnalyzerConfigFiles Include="analysis.cfg" /></ItemGroup></Project>\n',
    rule: /\[base\.no-analyzer-config-redirect\]/,
  },
  {
    name: 'a .ruleset file',
    file: `${MODULE_DIR}/off.ruleset`,
    content: '<RuleSet Name="off"><Rules AnalyzerId="X" RuleNamespace="X"><Rule Id="CA2000" Action="None" /></Rules></RuleSet>\n',
    rule: /\[base\.no-ruleset-file\]/,
  },
]);

describe('pre-tool-use dogfood: analyzer bypass forms from the real compiled rules', () => {
  let root;
  before(() => {
    root = createFixtureProject({ files: MODULE_FILES, commit: true });
    writeFiles(root, { 'rules/rules.compiled.json': fs.readFileSync(REPO_COMPILED_RULES, 'utf8') });
  });
  after(() => removeProject(root));

  for (const testCase of CASES) {
    it(`refuses a Write of ${testCase.name}`, () => {
      const { status, output } = runHookScript(SCRIPT, write(root, testCase.file, testCase.content), { projectRoot: root });
      assert.equal(status, 0);
      assert.equal(decisionOf(output), DENY);
      assert.match(reasonOf(output), testCase.rule);
    });
  }
});
