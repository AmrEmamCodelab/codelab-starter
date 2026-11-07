// The text twins of the endpoint access contract (base.endpoint-has-permission, base.minimal-api-has-permission) in
// rules/base.rules.yaml: every endpoint shape the contract test sees must be refused when it carries no gate, and
// every gated shape must pass. Compiles the real base rules in a throwaway workspace.
import { after, before, describe, test } from 'node:test';
import assert from 'node:assert/strict';
import { evaluateFile } from '../lib/match.mjs';
import { compileRepository } from '../lib/engine.mjs';
import { createWorkspace, removeWorkspace } from './helpers/workspace.mjs';

const CONTROLLER_RULE = 'base.endpoint-has-permission';
const MINIMAL_API_RULE = 'base.minimal-api-has-permission';
const CONTROLLER_FILE = 'backend/src/Modules/Orders/Api/OrdersController.cs';
const ENDPOINTS_FILE = 'backend/src/Modules/Orders/Api/OrdersEndpoints.cs';

const lines = (...text) => `${text.join('\n')}\n`;

/** A controller whose class attribute block is `classAttributes` and whose one action carries `actionAttributes`. */
const controller = ({ name = 'OrdersController', classAttributes = [], actionAttributes, declaration = 'public sealed class' }) =>
  lines(
    '[ApiController]',
    '[Route(OrdersRoutes.Root)]',
    ...classAttributes,
    `${declaration} ${name}(GetOrder getOrder) : ControllerBase`,
    '{',
    '    /// <summary>One order.</summary>',
    ...actionAttributes.map((attribute) => `    ${attribute}`),
    '    [ProducesResponseType(StatusCodes.Status200OK)]',
    '    public Task<ActionResult> GetAsync(PublicId<OrderIds> id) => getOrder.ExecuteAsync(id);',
    '}',
  );

const GATED_CLASS = ['[HasPermission(OrdersPermissionKeys.View)]'];

const minimalApi = (statement) =>
  lines(
    'public static class OrdersEndpoints',
    '{',
    '    public static void Map(IEndpointRouteBuilder endpoints) =>',
    `        ${statement}`,
    '}',
  );

describe('endpoint access text twins', () => {
  let root;
  let compiled;
  before(() => {
    root = createWorkspace();
    compiled = compileRepository(root).compiled;
  });
  after(() => removeWorkspace(root));

  const count = (relativePath, content, ruleId) =>
    evaluateFile(compiled, relativePath, content).find((result) => result.rule.id === ruleId)?.violations.length ?? 0;

  const UNGATED_ACTIONS = Object.freeze([
    ['a plain [HttpGet]', ['[HttpGet(OrdersRoutes.IdTemplate)]']],
    ['[AcceptVerbs] with [Route]', ['[AcceptVerbs("GET")]', '[Route(OrdersRoutes.IdTemplate)]']],
    ['[Route] alone (every verb)', ['[Route(OrdersRoutes.IdTemplate)]']],
    ['[AcceptVerbs] alone', ['[AcceptVerbs("GET", "POST")]']],
    ['[HttpGet] and [Route] together, counted once', ['[HttpGet]', '[Route(OrdersRoutes.IdTemplate)]']],
  ]);

  for (const [name, actionAttributes] of UNGATED_ACTIONS) {
    test(`refuses an ungated action with ${name}`, () => {
      assert.equal(count(CONTROLLER_FILE, controller({ actionAttributes }), CONTROLLER_RULE), 1);
    });

    test(`allows ${name} gated on the action`, () => {
      const gated = controller({ actionAttributes: [...actionAttributes, '[HasPermission(OrdersPermissionKeys.View)]'] });
      assert.equal(count(CONTROLLER_FILE, gated, CONTROLLER_RULE), 0);
    });

    test(`allows ${name} gated on the class`, () => {
      assert.equal(count(CONTROLLER_FILE, controller({ classAttributes: GATED_CLASS, actionAttributes }), CONTROLLER_RULE), 0);
    });
  }

  test('allows an allowlisted anonymous action written before its route', () => {
    const anonymous = controller({ actionAttributes: ['[AllowAnonymous]', '[AcceptVerbs("GET")]', '[Route(OrdersRoutes.IdTemplate)]'] });
    assert.equal(count(CONTROLLER_FILE, anonymous, CONTROLLER_RULE), 0);
  });

  test('a class-level [Route] is not an action', () => {
    const noActions = lines('[ApiController]', '[Route(OrdersRoutes.Root)]', 'public sealed class OrdersController : ControllerBase', '{', '}');
    assert.equal(count(CONTROLLER_FILE, noActions, CONTROLLER_RULE), 0);
  });

  for (const declaration of ['public  class', 'public\tsealed  class', 'class', 'internal sealed partial class']) {
    test(`a gated class does not cover the ungated controller declared after it as "${declaration.replace('\t', '\\t')}"`, () => {
      const gated = controller({ classAttributes: GATED_CLASS, actionAttributes: ['[HttpGet]'] });
      const ungated = controller({ name: 'OrdersExportController', declaration, actionAttributes: ['[HttpPost]'] });
      assert.equal(count(CONTROLLER_FILE, `${gated}\n${ungated}`, CONTROLLER_RULE), 1);
    });
  }

  const MINIMAL_CASES = Object.freeze([
    ['an ungated mapping', 'endpoints.MapGet(OrdersRoutes.Root, ListAsync);', 1],
    ['a gate word only inside a route constant', 'endpoints.MapPost(Routes.AllowAnonymousLookup, Handler);', 1],
    ['a gate word only inside a handler name', 'endpoints.MapPost(OrdersRoutes.Root, RequireAuthorizationHandler);', 1],
    ['a RequireAuthorization call', 'endpoints.MapGet(OrdersRoutes.Root, ListAsync).RequireAuthorization(OrdersPolicies.View);', 0],
    ['an AllowAnonymous call', 'endpoints.MapGet(OrdersRoutes.Root, ListAsync)\n            .AllowAnonymous();', 0],
    ['a HasPermission attribute in metadata', 'endpoints.MapGet(OrdersRoutes.Root, ListAsync).WithMetadata(new HasPermissionAttribute(OrdersPermissionKeys.View));', 0],
  ]);

  for (const [name, statement, expected] of MINIMAL_CASES) {
    test(`minimal API: ${name} ${expected ? 'is refused' : 'passes'}`, () => {
      assert.equal(count(ENDPOINTS_FILE, minimalApi(statement), MINIMAL_API_RULE), expected);
    });
  }
});
