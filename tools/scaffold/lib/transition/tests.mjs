// The test stubs new-transition writes into the module's test project: template tokens for a status test and for a
// transition test (one per from-status), and the test kit reference in the test project. Pure.
import { CSharp, Sides, TransitionPaths } from './constants.mjs';
import { isSameOrParentNamespace } from './csharp-source.mjs';

const FIXED_TEST_NAMESPACES = Object.freeze([CSharp.workflowNamespace, CSharp.applicationWorkflowNamespace, CSharp.testingNamespace]);
const TEST_REASON = '"A reason given in the test."';
const PATH_SEPARATOR = '/';
const CSPROJ_SEPARATOR = '\\';
const PROJECT_REFERENCE = /^([ \t]*)<ProjectReference\b[^\r\n]*$/gm;

/** The namespace of a module's generated workflow tests. */
export function testNamespaceOf(module) {
  return `Starter.Modules.${module}.Tests.Workflow`;
}

/**
 * `using X;` lines (each ending in a newline), sorted and unique, without the namespaces a template writes itself and
 * without parents of `testNamespace` (unnecessary usings fail the build, IDE0005).
 */
export function usingLines(namespaces, testNamespace) {
  return [...new Set(namespaces.filter((ns) => ns && !FIXED_TEST_NAMESPACES.includes(ns) && !isSameOrParentNamespace(ns, testNamespace)))]
    .sort((left, right) => left.localeCompare(right, 'en', { sensitivity: 'base' }))
    .map((ns) => `using ${ns};\n`)
    .join('');
}

function moduleNamespaces(sources, extra) {
  return [sources.definition.namespace, sources.statuses.namespace, ...extra];
}

function fact(name, body) {
  return `\n    [Fact]\n    public void ${name}() =>\n${body.map((line) => `        ${line}`).join('\n')};\n`;
}

/** Tokens of the status test (templates/transition/status). */
export function statusTestTokens({ module, sources }, statusRequest, status, testClass) {
  const extras = statusRequest.terminal
    ? fact(`${statusRequest.constName}_offers_no_action`, [
      `TransitionFixture.For(Catalog).From(${status.statusRef})`,
      '    .WithPermissions([.. KnownPermissions]).AsOwner()',
      '    .ExpectAllowedActions()',
    ])
    : '';
  const usings = usingLines(moduleNamespaces(sources, [sources.permissionKeysNamespace]), testNamespaceOf(module))
    + (statusRequest.terminal ? `using ${CSharp.testingNamespace};\n` : '');
  return {
    Module: module,
    Usings: usings,
    TestClass: testClass,
    DefinitionClass: sources.definition.name,
    CatalogSource: sources.catalogSourceClass,
    StatusKey: statusRequest.key,
    StatusPascal: statusRequest.constName,
    StatusRef: status.statusRef,
    TerminalAssert: statusRequest.terminal ? 'True' : 'False',
    ExtraTests: extras,
  };
}

/** Tokens of one transition test (templates/transition/rule) for the rule from `from`. */
export function ruleTestTokens({ module, sources }, request, rule, from, testClass) {
  const { rule: asked } = request;
  const ownerCall = asked.owner ? '.AsOwner()' : '';
  const reasonArgument = asked.reason === 'none' ? '' : `, ${TEST_REASON}`;
  let extras = '';
  if (asked.reason === 'required') {
    extras += fact(`${asked.actionConst}_needs_a_reason`, [
      `TransitionFixture.For(Catalog).From(${from.fromRef})`,
      `    .WithPermissions(${rule.permissionRef})${ownerCall}`,
      `    .Do(${rule.actionRef}).ExpectRefused(WorkflowRefusal.ReasonRequired)`,
    ]);
  }
  if (asked.owner) {
    extras += fact(`${asked.actionConst}_is_refused_to_a_non_owner`, [
      `TransitionFixture.For(Catalog).From(${from.fromRef})`,
      `    .WithPermissions(${rule.permissionRef}).AsNonOwner()`,
      `    .Do(${rule.actionRef}${reasonArgument}).ExpectRefused(WorkflowRefusal.Forbidden)`,
    ]);
  }
  return {
    Module: module,
    Usings: usingLines(moduleNamespaces(sources, [sources.actions?.namespace, sources.permissionKeysNamespace]), testNamespaceOf(module)),
    TestClass: testClass,
    DefinitionClass: sources.definition.name,
    CatalogSource: sources.catalogSourceClass,
    ActionKey: asked.actionKey,
    FromKey: from.fromKey,
    ToKey: asked.toKey,
    ActionPascal: asked.actionConst,
    FromPascal: from.fromRef.split('.').pop(),
    ToPascal: rule.toRef.split('.').pop(),
    ActionRef: rule.actionRef,
    FromRef: from.fromRef,
    ToRef: rule.toRef,
    PermissionRef: rule.permissionRef,
    OwnerCall: ownerCall,
    ReasonArgument: reasonArgument,
    TerminalCall: rule.terminalTo ? '.ExpectTerminal()' : '',
    Side: Sides[asked.side],
    IsOwner: String(asked.owner),
    ExtraTests: extras,
  };
}

/** Adds the Starter.Workflow.Testing reference after the last ProjectReference (once); null without any ProjectReference. */
export function addKitReference(csproj) {
  if (csproj.includes(TransitionPaths.kitProjectName)) return csproj;
  const references = [...csproj.matchAll(PROJECT_REFERENCE)];
  if (references.length === 0) return null;
  const last = references[references.length - 1];
  const eol = csproj.includes('\r\n') ? '\r\n' : '\n';
  const at = last.index + last[0].length;
  return `${csproj.slice(0, at)}${eol}${last[1]}<ProjectReference Include="${TransitionPaths.kitReference.split(PATH_SEPARATOR).join(CSPROJ_SEPARATOR)}" />${csproj.slice(at)}`;
}
