// Phase 9 "Bypass is impossible": work done around the scaffold skills is refused by the PreToolUse hook with a
// message that NAMES the skill ("Use the `<skill>` skill"), and CI (tools/rules-engine/check-content.mjs) fails on the
// same file. Runs the REAL rules/rules.compiled.json in a throwaway project under the OS temp dir.
import { after, before, describe, it } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
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
import { SkillReferencePattern } from '../lib/constants.mjs';

const SCRIPT = 'pre-tool-use.mjs';
const DENY = 'deny';
const REPO_ROOT = path.resolve(HOOKS_DIR, '..', '..');
const REPO_COMPILED_RULES = path.join(REPO_ROOT, 'rules', 'rules.compiled.json');
const CHECK_CONTENT = path.join(REPO_ROOT, 'tools', 'rules-engine', 'check-content.mjs');
const CHECK_CONTENT_VIOLATIONS = 1;
const CHECK_CONTENT_CLEAN = 0;

const MODULE = 'backend/src/Modules/Orders';
const FEATURE = 'frontend/starter-web/src/app/features/orders';
const MODULE_DOC = '# Orders\n\n## Purpose\nFixture.\n\n## Use\n-\n\n## Extend\n-\n\n## Rules\n-\n\n## Gotchas\n-\n';

const PROJECT_FILES = Object.freeze({
  [`${MODULE}/module.rules.yaml`]: 'module: Orders\n',
  [`${MODULE}/MODULE.md`]: MODULE_DOC,
  [`${FEATURE}/module.rules.yaml`]: 'module: OrdersUi\n',
  [`${FEATURE}/MODULE.md`]: MODULE_DOC,
});

const lines = (...text) => `${text.join('\n')}\n`;

const UNGATED_CONTROLLER = lines(
  'namespace Starter.Modules.Orders.Api;',
  '',
  '[ApiController]',
  '[Route(OrdersRoutes.Root)]',
  'public sealed class OrdersController(GetOrder getOrder) : ControllerBase',
  '{',
  '    /// <summary>One order.</summary>',
  '    [HttpGet(OrdersRoutes.IdTemplate)]',
  '    [ProducesResponseType(StatusCodes.Status200OK)]',
  '    public Task<ActionResult> GetAsync(PublicId<OrderIds> id) => getOrder.ExecuteAsync(id);',
  '}',
);

const ACTION_GATED_CONTROLLER = UNGATED_CONTROLLER.replace(
  '    [HttpGet(OrdersRoutes.IdTemplate)]\n',
  '    [HttpGet(OrdersRoutes.IdTemplate)]\n    [HasPermission(OrdersPermissionKeys.View)]\n',
);

const CLASS_GATED_CONTROLLER = UNGATED_CONTROLLER.replace(
  '[Route(OrdersRoutes.Root)]\n',
  '[Route(OrdersRoutes.Root)]\n[HasPermission(OrdersPermissionKeys.View)]\n',
);

const ANONYMOUS_CONTROLLER = UNGATED_CONTROLLER.replace(
  '    [HttpGet(OrdersRoutes.IdTemplate)]\n',
  '    [AllowAnonymous]\n    [HttpGet(OrdersRoutes.IdTemplate)]\n',
);

const UNGATED_MINIMAL_API = lines(
  'public static class OrdersEndpoints',
  '{',
  '    public static void Map(IEndpointRouteBuilder endpoints) =>',
  '        endpoints.MapGet(OrdersRoutes.Root, ListAsync).Produces(StatusCodes.Status200OK);',
  '}',
);

const GATED_MINIMAL_API = UNGATED_MINIMAL_API.replace('.Produces(', '.RequireAuthorization(OrdersPolicies.View).Produces(');

const HAND_STATUS_CHANGE = lines(
  'public sealed class ApproveOrder',
  '{',
  '    public void Execute(Order order) => order.Status = OrderStatuses.Approved;',
  '}',
);

const CATALOG = lines(
  'public static class OrderWorkflow',
  '{',
  '    public static TransitionCatalog Catalog { get; } = new(OrderRecord.RecordType, Statuses, Rules);',
  '    private static TransitionRule Staff(string action, int from, int to) => new(action, from, to);',
  '}',
);

const LOOKUP_SEED = lines(
  'public sealed class OrderKindsSeeder',
  '{',
  '    public void Seed(MigrationBuilder builder) => LookupSeed.InsertType(builder, LookupTypes.OrderKinds, Names, Items);',
  '}',
);

const RAW_TEXT_TEMPLATE = '<section>\n  <h2>Order details</h2>\n</section>\n';
const TRANSLATED_TEMPLATE = "<section>\n  <h2>{{ 'orders.details.title' | translate }}</h2>\n</section>\n";

const write = (root, relativePath, content) =>
  toolInput({ projectRoot: root, toolName: 'Write', toolInputFields: { file_path: `${root}/${relativePath}`, content } });

function decide(root, relativePath, content) {
  const { status, output } = runHookScript(SCRIPT, write(root, relativePath, content), { projectRoot: root });
  assert.equal(status, 0);
  return output;
}

function assertRefusedNaming(output, { ruleId, skill }) {
  assert.equal(decisionOf(output), DENY);
  assert.match(reasonOf(output), new RegExp(`\\[${ruleId.replace(/\./g, '\\.')}\\]`));
  assert.match(reasonOf(output), new RegExp(`Use the \`${skill}\` skill`));
  assert.equal(reasonOf(output).match(SkillReferencePattern)?.[1], skill, 'the first skill named is the one to use');
}

function runCheckContent(root) {
  return spawnSync(process.execPath, [CHECK_CONTENT, '--root', root], { encoding: 'utf8', windowsHide: true });
}

/** Each bypass: the file a hand edit writes, the rule, the skill the refusal must name, and a corrected version. */
const BYPASSES = Object.freeze([
  {
    name: 'an endpoint without [HasPermission]',
    file: `${MODULE}/Api/OrdersController.cs`, bad: UNGATED_CONTROLLER, good: ACTION_GATED_CONTROLLER,
    ruleId: 'base.endpoint-has-permission', skill: 'new-endpoint',
  },
  {
    name: 'a minimal-API mapping without a gate',
    file: `${MODULE}/Api/OrdersEndpoints.cs`, bad: UNGATED_MINIMAL_API, good: GATED_MINIMAL_API,
    ruleId: 'base.minimal-api-has-permission', skill: 'new-endpoint',
  },
  {
    name: 'a status change outside the transition catalog',
    file: `${MODULE}/Application/ApproveOrder.cs`, bad: HAND_STATUS_CHANGE, good: lines('public sealed class ApproveOrder { }'),
    ruleId: 'base.transition-in-catalog', skill: 'new-transition',
  },
  {
    name: 'a lookup seeded outside a migration',
    file: `${MODULE}/Infrastructure/OrderKindsSeeder.cs`, bad: LOOKUP_SEED, good: lines('public sealed class OrderKindsSeeder { }'),
    ruleId: 'base.lookup-seeded-by-migration', skill: 'new-lookup',
  },
  {
    name: 'a screen template with raw user-visible text',
    file: `${FEATURE}/details/order-details.component.html`, bad: RAW_TEXT_TEMPLATE, good: TRANSLATED_TEMPLATE,
    ruleId: 'base.fe-raw-text-in-template', skill: 'new-screen',
  },
]);

describe('skill routing: hand-made work is refused and the refusal names the skill', () => {
  let root;
  before(() => {
    root = createFixtureProject({ files: PROJECT_FILES, commit: true });
    writeFiles(root, { 'rules/rules.compiled.json': fs.readFileSync(REPO_COMPILED_RULES, 'utf8') });
  });
  after(() => removeProject(root));

  for (const bypass of BYPASSES) {
    it(`refuses ${bypass.name}, naming ${bypass.skill}`, () => {
      assertRefusedNaming(decide(root, bypass.file, bypass.bad), bypass);
    });

    it(`allows the corrected version of ${bypass.name}`, () => {
      assert.equal(decisionOf(decide(root, bypass.file, bypass.good)), null);
    });
  }

  it('refuses a hand-made module folder (no module.rules.yaml), naming new-module', () => {
    const output = decide(root, 'backend/src/Modules/Permits/Domain/Permit.cs', lines('public sealed class Permit { }'));
    assertRefusedNaming(output, { ruleId: 'hooks.module-without-rules', skill: 'new-module' });
  });

  it('allows an action gated on its controller class, and an allowlisted anonymous action', () => {
    assert.equal(decisionOf(decide(root, `${MODULE}/Api/OrdersController.cs`, CLASS_GATED_CONTROLLER)), null);
    assert.equal(decisionOf(decide(root, `${MODULE}/Api/OrdersController.cs`, ANONYMOUS_CONTROLLER)), null);
  });

  it('refuses only the ungated controller of a file whose other controller is gated on its class', () => {
    const twoControllers = `${CLASS_GATED_CONTROLLER}\n${UNGATED_CONTROLLER.replace('namespace Starter.Modules.Orders.Api;\n\n', '').replace(/OrdersController/g, 'OrdersExportController')}`;
    assertRefusedNaming(decide(root, `${MODULE}/Api/OrdersController.cs`, twoControllers), {
      ruleId: 'base.endpoint-has-permission', skill: 'new-endpoint',
    });
  });

  it('allows the catalog itself (Application/Workflow/<Entity>Workflow.cs) and a seed inside a migration', () => {
    assert.equal(decisionOf(decide(root, `${MODULE}/Application/Workflow/OrderWorkflow.cs`, CATALOG)), null);
    const migration = `${MODULE}/Infrastructure/Persistence/Migrations/20261107000000_AddOrderKindsLookup.cs`;
    assert.equal(decisionOf(decide(root, migration, LOOKUP_SEED)), null);
  });

  it('refuses a transition catalog built outside the workflow class, naming new-transition', () => {
    assertRefusedNaming(decide(root, `${MODULE}/Application/OrderRules.cs`, CATALOG), {
      ruleId: 'base.transition-in-catalog', skill: 'new-transition',
    });
  });
});

describe('skill routing: CI agrees (check-content.mjs fails on the same files)', () => {
  let root;
  before(() => {
    root = createFixtureProject({ files: PROJECT_FILES });
    writeFiles(root, { 'rules/rules.compiled.json': fs.readFileSync(REPO_COMPILED_RULES, 'utf8') });
  });
  after(() => removeProject(root));

  it('a clean fixture passes the content check', () => {
    const result = runCheckContent(root);
    assert.equal(result.status, CHECK_CONTENT_CLEAN, result.stderr);
  });

  it('every bypass fixture fails the content check with its rule and the skill-naming fix', () => {
    writeFiles(root, Object.fromEntries(BYPASSES.map((bypass) => [bypass.file, bypass.bad])));
    const result = runCheckContent(root);
    assert.equal(result.status, CHECK_CONTENT_VIOLATIONS);
    for (const bypass of BYPASSES) {
      assert.match(result.stderr, new RegExp(`${bypass.file.replace(/[.]/g, '\\.')}: \\[${bypass.ruleId.replace(/\./g, '\\.')}\\]`), bypass.name);
      assert.match(result.stderr, new RegExp(`Use the \`${bypass.skill}\` skill`), bypass.name);
    }
  });

  it('the corrected files pass the content check again', () => {
    writeFiles(root, Object.fromEntries(BYPASSES.map((bypass) => [bypass.file, bypass.good])));
    const result = runCheckContent(root);
    assert.equal(result.status, CHECK_CONTENT_CLEAN, result.stderr);
  });
});
