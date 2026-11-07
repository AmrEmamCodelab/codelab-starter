import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, it } from 'node:test';
import { newEndpoint } from '../new-endpoint.mjs';
import { newModule } from '../new-module.mjs';
import { newPermission } from '../new-permission.mjs';
import { runCli } from '../lib/cli.mjs';
import { ExitCode, SKIP_GENERATE_ENV } from '../lib/constants.mjs';
import { renderTemplateSet, TEMPLATES_ROOT } from '../lib/templates.mjs';
import { catalogCommands, downstreamCommands, regenerate, resolveDotnet } from '../lib/workspace.mjs';
import { moduleRulePrefix as rulesEnginePrefix } from '../../rules-engine/lib/validate.mjs';

const SKIP = '--skip-generate';
const DRY_RUN = '--dry-run';
/** The ruleId pattern of rules/rules.schema.json. */
const RULE_ID_SCHEMA = /^[a-z][a-z0-9]*(\.[a-z0-9]+(-[a-z0-9]+)*)+$/;
const ACCESS_DOC = '# Access module\n\n## Purpose\n\nAccess.\n\n## Use\n\n- Roles.\n\n## Extend\n\nMore.\n';
const ACCESS_RULES = 'schemaVersion: 1\nmodule: Access\nkind: dotnet\nrequired:\n  - id: access.thin-endpoints\n    statement: Thin.\n    enforcedBy: [review]\nrecipes: [docs/recipes/add-endpoint.md]\n';
const MODULE_DIR = 'backend/src/Modules/Demo';
const TESTS_DIR = 'backend/tests/Modules/Starter.Modules.Demo.Tests';

const SOLUTION = '<Solution>\n  <Folder Name="/tools/">\n    <Project Path="tools/x/X.csproj" />\n  </Folder>\n</Solution>\n';
/** MSBuild Include paths use backslashes; built from parts so no literal reads like a share path. */
const msbuildPath = (...parts) => parts.join(String.fromCharCode(92));
const HOST = `<Project Sdk="Microsoft.NET.Sdk.Web">\n  <ItemGroup Label="Modules">\n    <ProjectReference Include="${msbuildPath('..', 'Modules', 'Access', 'Starter.Modules.Access.csproj')}" />\n  </ItemGroup>\n</Project>\n`;
const ACCESS_KEYS = 'namespace Starter.Modules.Access.Application;\n\npublic static class AccessPermissionKeys\n{\n    public const string DashboardView = "dashboard.view";\n}\n';
const ACCESS_CATALOG = 'public sealed class AccessPermissionCatalogSource\n{\n    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =\n    [\n        new(Key: AccessPermissionKeys.DashboardView, Description: "Open the dashboard."),\n    ];\n}\n';

let root;

function write(relative, content) {
  fs.mkdirSync(path.dirname(path.join(root, relative)), { recursive: true });
  fs.writeFileSync(path.join(root, relative), content);
}

function read(relative) {
  return fs.readFileSync(path.join(root, relative), 'utf8');
}

function snapshot() {
  const files = {};
  const walk = (dir) => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else files[path.relative(root, full)] = fs.readFileSync(full, 'utf8');
    }
  };
  walk(root);
  return files;
}

/** Every template set: a folder under templates/ holding a backend/ tree (transition/ nests its two sets). */
function templateSets(dir = TEMPLATES_ROOT, prefix = '') {
  return fs.readdirSync(dir, { withFileTypes: true }).filter((entry) => entry.isDirectory()).flatMap((entry) => {
    const set = prefix ? `${prefix}/${entry.name}` : entry.name;
    const full = path.join(dir, entry.name);
    return fs.existsSync(path.join(full, 'backend')) ? [set] : templateSets(full, set);
  });
}

function exitOf(body) {
  return runCli('usage', body, { out: () => {}, err: () => {} });
}

beforeEach(() => {
  root = fs.mkdtempSync(path.join(os.tmpdir(), 'scaffold-test-'));
  write('codelab-starter.slnx', SOLUTION);
  write('backend/src/Starter.Api/Starter.Api.csproj', HOST);
  write('codelab.starter.json', JSON.stringify({ removedModules: ['Legacy'] }));
  write('backend/src/Modules/Access/Starter.Modules.Access.csproj', '<Project />\n');
  write('backend/src/Modules/Access/Application/AccessPermissionKeys.cs', ACCESS_KEYS);
  write('backend/src/Modules/Access/Application/AccessPermissionCatalogSource.cs', ACCESS_CATALOG);
  write('backend/src/Modules/Access/MODULE.md', ACCESS_DOC);
  write('backend/src/Modules/Access/module.rules.yaml', ACCESS_RULES);
});

afterEach(() => fs.rmSync(root, { recursive: true, force: true }));

describe('new-module', () => {
  it('creates the full anatomy, the test project, the solution entries and the host reference', () => {
    const lines = newModule(['--name', 'Demo', '--root', root, SKIP]);
    for (const file of [
      'Starter.Modules.Demo.csproj', 'DemoModule.cs', 'module.rules.yaml', 'MODULE.md',
      'Application/DemoPermissionKeys.cs', 'Application/DemoPermissionCatalogSource.cs', 'Application/DemoErrorCodes.cs',
      'Infrastructure/DemoModuleRegistration.cs', 'Api/DemoRoutes.cs', 'Api/DemoIds.cs', 'Domain/DemoLimits.cs',
    ]) {
      assert.ok(fs.existsSync(path.join(root, MODULE_DIR, file)), file);
      assert.ok(lines.includes(`created ${MODULE_DIR}/${file}`), file);
    }
    assert.ok(fs.existsSync(path.join(root, TESTS_DIR, 'Starter.Modules.Demo.Tests.csproj')));
    assert.ok(fs.existsSync(path.join(root, TESTS_DIR, 'DemoModuleTests.cs')));
    assert.match(read('codelab-starter.slnx'), /<Folder Name="\/backend\/">\n    <Project Path="backend\/src\/Modules\/Demo\/Starter.Modules.Demo.csproj" \/>\n    <Project Path="backend\/tests\/Modules\/Starter.Modules.Demo.Tests\/Starter.Modules.Demo.Tests.csproj" \/>/);
    assert.ok(read('backend/src/Starter.Api/Starter.Api.csproj').includes(`<ProjectReference Include="${msbuildPath('..', 'Modules', 'Demo', 'Starter.Modules.Demo.csproj')}" />`));
  });

  it('writes a MODULE.md with the five sections and a rules file naming the module', () => {
    newModule(['--name', 'ServiceRequests', '--root', root, SKIP]);
    const moduleMd = read('backend/src/Modules/ServiceRequests/MODULE.md');
    for (const section of ['Purpose', 'Use', 'Extend', 'Rules', 'Gotchas']) assert.match(moduleMd, new RegExp(`^## ${section}$`, 'm'));
    const rules = read('backend/src/Modules/ServiceRequests/module.rules.yaml');
    assert.match(rules, /^module: ServiceRequests$/m);
    // Rule ids take the rules engine's module prefix (the lower-cased name), never the kebab name.
    assert.match(rules, /id: servicerequests\.thin-endpoints/);
    for (const [, id] of rules.matchAll(/^\s*- id: (\S+)$/gm)) {
      assert.ok(id.startsWith(`${rulesEnginePrefix('ServiceRequests')}.`), id);
      assert.match(id, RULE_ID_SCHEMA);
    }
    assert.match(read('backend/src/Modules/ServiceRequests/Application/ServiceRequestsPermissionKeys.cs'), /View = "service-requests.view"/);
  });

  for (const [name, reason] of [['Demo', 'existing'], ['Legacy', 'removedModules'], ['Api', 'kernel'], ['demo', 'not Pascal']]) {
    it(`refuses ${name} (${reason}) and changes nothing`, () => {
      if (reason === 'existing') newModule(['--name', 'Demo', '--root', root, SKIP]);
      const before = snapshot();
      assert.equal(exitOf(() => newModule(['--name', name, '--root', root, SKIP])), ExitCode.refused);
      assert.deepEqual(snapshot(), before);
    });
  }

  it('maps a missing --name or an unknown flag to the usage exit code', () => {
    assert.equal(exitOf(() => newModule(['--root', root])), ExitCode.usage);
    assert.equal(exitOf(() => newModule(['--name', 'Demo', '--bogus', 'x'])), ExitCode.usage);
  });
});

describe('new-endpoint', () => {
  beforeEach(() => newModule(['--name', 'Demo', '--root', root, SKIP]));

  it('scaffolds a GET with the view permission, a not-found refusal, the store and the tests', () => {
    newEndpoint(['--module', 'Demo', '--name', 'GetWidget', '--method', 'GET', '--route', 'widgets', '--root', root, SKIP]);
    const controller = read(`${MODULE_DIR}/Api/GetWidgetController.cs`);
    assert.match(controller, /\[HasPermission\(DemoPermissionKeys.View\)\]/);
    assert.match(controller, /\[HttpGet\(DemoRoutes.IdTemplate\)\]/);
    assert.match(controller, /\[property: WireId\] PublicId<DemoIds> Id/);
    assert.match(read(`${MODULE_DIR}/Application/GetWidget.cs`), /Refusal.NotFound\(DemoErrorCodes.GetWidgetNotFound\)/);
    assert.match(read(`${MODULE_DIR}/Application/DemoErrorCodes.cs`), /GetWidgetNotFound = "DEMO_GET_WIDGET_NOT_FOUND"/);
    assert.match(read(`${MODULE_DIR}/Infrastructure/DemoModuleRegistration.cs`), /services.AddScoped<IGetWidgetStore, GetWidgetStore>\(\);/);
    assert.ok(fs.existsSync(path.join(root, TESTS_DIR, 'GetWidgetTests.cs')));
  });

  it('scaffolds a POST with a validator, a manage permission added on demand and its registration', () => {
    newEndpoint(['--module', 'Demo', '--name', 'CreateWidget', '--method', 'post', '--route', 'widgets', '--root', root, SKIP]);
    assert.match(read(`${MODULE_DIR}/Application/DemoPermissionKeys.cs`), /Manage = "demo.manage"/);
    assert.match(read(`${MODULE_DIR}/Application/DemoPermissionCatalogSource.cs`), /DemoPermissionKeys.Manage/);
    assert.match(read(`${MODULE_DIR}/Application/CreateWidget.cs`), /class CreateWidgetValidator : IValidator<CreateWidgetCommand>/);
    const registration = read(`${MODULE_DIR}/Infrastructure/DemoModuleRegistration.cs`);
    assert.match(registration, /services.AddScoped<IValidator<CreateWidgetCommand>, CreateWidgetValidator>\(\);/);
    assert.match(registration, /^using Starter.Application.Validation;$/m);
  });

  it('scaffolds PUT and DELETE, and uses an explicit --permission', () => {
    newEndpoint(['--module', 'Demo', '--name', 'RenameWidget', '--method', 'PUT', '--route', 'widgets', '--permission', 'rename', '--root', root, SKIP]);
    newEndpoint(['--module', 'Demo', '--name', 'DeleteWidget', '--method', 'DELETE', '--route', 'widgets', '--root', root, SKIP]);
    assert.match(read(`${MODULE_DIR}/Api/RenameWidgetController.cs`), /\[HasPermission\(DemoPermissionKeys.Rename\)\][\s\S]*\[HttpPut\(DemoRoutes.IdTemplate\)\]/);
    assert.match(read(`${MODULE_DIR}/Api/DeleteWidgetController.cs`), /ToNoContentResult\(\)/);
    assert.match(read(`${MODULE_DIR}/Application/DemoPermissionKeys.cs`), /Rename = "demo.rename"/);
  });

  it('is idempotent for identical arguments and refuses a different method under the same name', () => {
    const args = ['--module', 'Demo', '--name', 'GetWidget', '--method', 'GET', '--route', 'widgets', '--root', root, SKIP];
    newEndpoint(args);
    const before = snapshot();
    assert.match(newEndpoint(args)[0], /^unchanged /);
    assert.deepEqual(snapshot(), before);
    assert.equal(exitOf(() => newEndpoint(['--module', 'Demo', '--name', 'GetWidget', '--method', 'DELETE', '--route', 'widgets', '--root', root, SKIP])), ExitCode.refused);
    assert.deepEqual(snapshot(), before);
  });

  it('refuses an unknown module, a bad method or route, and missing arguments', () => {
    assert.equal(exitOf(() => newEndpoint(['--module', 'Nope', '--name', 'A', '--method', 'GET', '--route', 'a', '--root', root, SKIP])), ExitCode.refused);
    assert.equal(exitOf(() => newEndpoint(['--module', 'Demo', '--name', 'A', '--method', 'PATCH', '--route', 'a', '--root', root, SKIP])), ExitCode.refused);
    assert.equal(exitOf(() => newEndpoint(['--module', 'Demo', '--name', 'A', '--method', 'GET', '--route', '/a', '--root', root, SKIP])), ExitCode.refused);
    assert.equal(exitOf(() => newEndpoint(['--module', 'Demo', '--root', root])), ExitCode.usage);
  });

  it('scaffolds a POST on the module root, with "." or an empty route, without the unused hosting using', () => {
    newEndpoint(['--module', 'Demo', '--name', 'CreateDemo', '--method', 'POST', '--route', '.', '--root', root, SKIP]);
    const controller = read(`${MODULE_DIR}/Api/CreateDemoController.cs`);
    assert.match(controller, /public const string Route = DemoRoutes\.Root;/);
    assert.match(controller, /<c>POST \/api\/demo<\/c>/);
    assert.doesNotMatch(controller, /using Starter\.Infrastructure\.Hosting;/);
    assert.match(read(`${TESTS_DIR}/CreateDemoTests.cs`), /Assert\.Equal\("api\/demo", controller/);

    newEndpoint(['--module', 'Demo', '--name', 'CreateOther', '--method', 'POST', '--route', '', '--root', root, SKIP]);
    assert.match(read(`${MODULE_DIR}/Api/CreateOtherController.cs`), /public const string Route = DemoRoutes\.Root;/);
  });

  it('scaffolds a GET whose route puts the id before a suffix', () => {
    newEndpoint(['--module', 'Demo', '--name', 'GetDemoDraft', '--method', 'GET', '--route', '{id}/draft', '--root', root, SKIP]);
    const controller = read(`${MODULE_DIR}/Api/GetDemoDraftController.cs`);
    assert.match(controller, /public const string Route = DemoRoutes\.Root;/);
    assert.match(controller, /\[HttpGet\(DemoRoutes\.IdTemplate \+ ApiRoutes\.Separator \+ "draft"\)\]/);
    assert.match(controller, /^using Starter\.Infrastructure\.Hosting;$/m);
    assert.match(controller, /<c>GET \/api\/demo\/\{id\}\/draft<\/c>/);
    assert.match(read(`${TESTS_DIR}/GetDemoDraftTests.cs`), /Assert\.Equal\("api\/demo", controller/);

    newEndpoint(['--module', 'Demo', '--name', 'ArchiveWidget', '--method', 'PUT', '--route', 'widgets/{id}/archive', '--root', root, SKIP]);
    const put = read(`${MODULE_DIR}/Api/ArchiveWidgetController.cs`);
    assert.match(put, /public const string Route = DemoRoutes\.Root \+ ApiRoutes\.Separator \+ "widgets";/);
    assert.match(put, /\[HttpPut\(DemoRoutes\.IdTemplate \+ ApiRoutes\.Separator \+ "archive"\)\]/);
  });

  it('keeps the collection route with the id last, and refuses an id in a POST route or a second id', () => {
    newEndpoint(['--module', 'Demo', '--name', 'GetWidget', '--method', 'GET', '--route', 'widgets', '--root', root, SKIP]);
    const controller = read(`${MODULE_DIR}/Api/GetWidgetController.cs`);
    assert.match(controller, /public const string Route = DemoRoutes\.Root \+ ApiRoutes\.Separator \+ "widgets";/);
    assert.match(controller, /<c>GET \/api\/demo\/widgets\/\{id\}<\/c>/);
    const before = snapshot();
    assert.equal(exitOf(() => newEndpoint(['--module', 'Demo', '--name', 'SubmitDemo', '--method', 'POST', '--route', '{id}/submit', '--root', root, SKIP])), ExitCode.refused);
    assert.equal(exitOf(() => newEndpoint(['--module', 'Demo', '--name', 'GetPair', '--method', 'GET', '--route', '{id}/x/{id}', '--root', root, SKIP])), ExitCode.refused);
    assert.equal(exitOf(() => newEndpoint(['--module', 'Demo', '--name', 'GetOther', '--method', 'GET', '--route', '{key}/x', '--root', root, SKIP])), ExitCode.refused);
    assert.deepEqual(snapshot(), before);
  });
});

describe('new-permission', () => {
  it('adds a key to a scaffolded module and is idempotent', () => {
    newModule(['--name', 'Demo', '--root', root, SKIP]);
    const args = ['--module', 'Demo', '--action', 'approve-request', '--description', 'Approve a request.', '--root', root, SKIP];
    newPermission(args);
    assert.match(read(`${MODULE_DIR}/Application/DemoPermissionKeys.cs`), /\/\/\/ <summary>Approve a request.<\/summary>\n    public const string ApproveRequest = "demo.approve-request";/);
    assert.match(read(`${MODULE_DIR}/Application/DemoPermissionCatalogSource.cs`), /new\(Key: DemoPermissionKeys.ApproveRequest, Description: "Approve a request."\),/);
    const before = snapshot();
    assert.match(newPermission(args)[0], /^unchanged /);
    assert.deepEqual(snapshot(), before);
  });

  it('adds a key to a hand-written module through the fallback anchors', () => {
    newPermission(['--module', 'Access', '--action', 'audit-view', '--root', root, SKIP]);
    assert.match(read('backend/src/Modules/Access/Application/AccessPermissionKeys.cs'), /AuditView = "access.audit-view";\n\}/);
    assert.match(read('backend/src/Modules/Access/Application/AccessPermissionCatalogSource.cs'), /new\(Key: AccessPermissionKeys.AuditView, Description: "Audit view \(Access module\)."\),\n    \];/);
  });

  it('names the frontend constant api:gen will generate', () => {
    const lines = newPermission(['--module', 'Access', '--action', 'audit-view', '--root', root, SKIP]);
    assert.ok(lines.includes('frontend key: PermissionKeys.AccessAuditView ("access.audit-view")'));
  });

  it('with --no-gen writes the C# catalog, runs nothing and prints the exact two regenerate commands', () => {
    const env = { CODELAB_DOTNET: 'C:/sdk/dotnet.exe' };
    const lines = newPermission(['--module', 'Access', '--action', 'audit-view', '--root', root, '--no-gen'], env);
    assert.deepEqual(lines, [
      'changed backend/src/Modules/Access/Application/AccessPermissionKeys.cs',
      'changed backend/src/Modules/Access/Application/AccessPermissionCatalogSource.cs',
      'changed backend/src/Modules/Access/MODULE.md',
      'changed backend/src/Modules/Access/module.rules.yaml',
      'frontend key: PermissionKeys.AccessAuditView ("access.audit-view")',
      'next: C:/sdk/dotnet.exe run --project backend/tools/Starter.DocsGen/Starter.DocsGen.csproj -- --out docs/generated',
      'next: npm run api:gen --workspace frontend/starter-web',
    ]);
    assert.deepEqual(catalogCommands(env), lines.slice(-2).map((line) => line.slice('next: '.length)));
    assert.match(newPermission(['--module', 'Access', '--action', 'audit-view', '--root', root, '--no-gen'], env)[0], /^unchanged /);
  });

  it('refuses a non-kebab action and an unknown module', () => {
    assert.equal(exitOf(() => newPermission(['--module', 'Access', '--action', 'AuditView', '--root', root, SKIP])), ExitCode.refused);
    assert.equal(exitOf(() => newPermission(['--module', 'Ghost', '--action', 'view', '--root', root, SKIP])), ExitCode.refused);
  });
});

describe('docs and rules move with the code', () => {
  it('new-endpoint adds its MODULE.md note under Use and keeps the add-endpoint recipe, once', () => {
    newModule(['--name', 'Demo', '--root', root, SKIP]);
    const args = ['--module', 'Demo', '--name', 'GetWidget', '--method', 'GET', '--route', 'widgets', '--root', root, SKIP];
    const lines = newEndpoint(args);
    const doc = read(`${MODULE_DIR}/MODULE.md`);
    const note = '- Endpoint `GET /api/demo/widgets/{id}` (`GetWidget`, permission `demo.view`, refusal `DEMO_GET_WIDGET_NOT_FOUND`), added with tools/scaffold/new-endpoint.mjs; tests `GetWidgetTests.cs`.';
    assert.ok(doc.includes(note));
    assert.ok(doc.indexOf(note) < doc.indexOf('## Extend'), 'the note sits in the Use section');
    assert.ok(lines.includes(`changed ${MODULE_DIR}/MODULE.md`));
    assert.match(read(`${MODULE_DIR}/module.rules.yaml`), /^recipes: \[docs\/recipes\/add-endpoint\.md\]$/m);
    newEndpoint(args);
    assert.equal(read(`${MODULE_DIR}/MODULE.md`).split(note).length, 2, 'the note is written once');
  });

  it('new-permission documents a new key and its recipe, and nothing for a key that exists', () => {
    newPermission(['--module', 'Access', '--action', 'audit-view', '--description', 'See the audit trail.', '--root', root, SKIP]);
    assert.ok(read('backend/src/Modules/Access/MODULE.md').includes('- Permission `access.audit-view` (`AccessPermissionKeys.AuditView`): See the audit trail. Added with tools/scaffold/new-permission.mjs.\n\n## Extend'));
    assert.match(read('backend/src/Modules/Access/module.rules.yaml'), /^recipes: \[docs\/recipes\/add-endpoint\.md, docs\/recipes\/add-permission\.md\]$/m);
    const before = snapshot();
    newPermission(['--module', 'Access', '--action', 'audit-view', '--root', root, SKIP]);
    assert.deepEqual(snapshot(), before);
  });

  it('a module without MODULE.md or rules file gets a note naming what to add by hand, and the code still lands', () => {
    fs.rmSync(path.join(root, 'backend/src/Modules/Access/MODULE.md'));
    fs.rmSync(path.join(root, 'backend/src/Modules/Access/module.rules.yaml'));
    const lines = newPermission(['--module', 'Access', '--action', 'audit-view', '--root', root, SKIP]);
    assert.ok(lines.some((line) => line.startsWith('note: no backend/src/Modules/Access/MODULE.md: add these lines')));
    assert.ok(lines.some((line) => line.startsWith('note: no backend/src/Modules/Access/module.rules.yaml: list docs/recipes/add-permission.md')));
    assert.match(read('backend/src/Modules/Access/Application/AccessPermissionKeys.cs'), /AuditView/);
  });
});

describe('--dry-run and the refresh instructions', () => {
  const env = { CODELAB_DOTNET: 'C:/sdk/dotnet.exe' };
  const docsGen = 'next: C:/sdk/dotnet.exe run --project backend/tools/Starter.DocsGen/Starter.DocsGen.csproj -- --out docs/generated';

  it('new-module --dry-run lists every file, writes nothing and prints the rules compile and DocsGen', () => {
    const before = snapshot();
    const lines = newModule(['--name', 'Demo', '--root', root, DRY_RUN], env);
    assert.deepEqual(snapshot(), before);
    assert.ok(lines.includes(`would create ${MODULE_DIR}/module.rules.yaml`));
    assert.ok(lines.includes(`would create ${MODULE_DIR}/MODULE.md`));
    assert.ok(lines.includes(`would create ${TESTS_DIR}/DemoModuleTests.cs`));
    assert.ok(lines.includes('would change codelab-starter.slnx'));
    assert.deepEqual(lines.slice(-3), ['note: dry run: nothing was written', 'next: node tools/rules-engine/compile.mjs', docsGen]);
  });

  it('new-endpoint and new-permission --dry-run write nothing and add api:gen to the refresh', () => {
    newModule(['--name', 'Demo', '--root', root, SKIP]);
    const before = snapshot();
    const endpoint = newEndpoint(['--module', 'Demo', '--name', 'GetWidget', '--method', 'GET', '--route', 'widgets', '--root', root, DRY_RUN], env);
    const permission = newPermission(['--module', 'Demo', '--action', 'approve', '--root', root, DRY_RUN], env);
    assert.deepEqual(snapshot(), before);
    assert.ok(endpoint.includes(`would create ${TESTS_DIR}/GetWidgetTests.cs`));
    assert.ok(endpoint.includes(`would change ${MODULE_DIR}/MODULE.md`));
    assert.ok(permission.includes(`would change ${MODULE_DIR}/Application/DemoPermissionKeys.cs`));
    for (const lines of [endpoint, permission]) {
      assert.deepEqual(lines.slice(-3), ['next: node tools/rules-engine/compile.mjs', docsGen, 'next: npm run api:gen --workspace frontend/starter-web']);
    }
  });

  it('--skip-generate prints the refresh it skipped', () => {
    const lines = newModule(['--name', 'Demo', '--root', root, SKIP], env);
    assert.deepEqual(lines.slice(-2), ['next: node tools/rules-engine/compile.mjs', docsGen]);
  });

  it('downstreamCommands lists the rules compile, DocsGen and api:gen on request', () => {
    assert.deepEqual(downstreamCommands({ apiClient: false, docs: false, env }), ['node tools/rules-engine/compile.mjs']);
    assert.equal(downstreamCommands({ apiClient: true, env }).length, 3);
    assert.deepEqual(catalogCommands(env), downstreamCommands({ apiClient: true, env }).slice(1));
  });
});

describe('templates and downstream generators', () => {
  it('renders every template set without an unknown token', () => {
    const values = { Module: 'Demo', ModuleKebab: 'demo', ModuleUpper: 'DEMO', Name: 'GetWidget', ControllerRoute: 'DemoRoutes.Root + ApiRoutes.Separator + "widgets"', ControllerRouteText: 'api/demo/widgets', IdTemplate: 'DemoRoutes.IdTemplate', HostingUsing: 'using Starter.Infrastructure.Hosting;\n', PermissionConst: 'View', ErrorConst: 'GetWidgetNotFound', HttpRoute: 'GET /api/demo/widgets/{id}',
      IdsClass: 'KindIds', TypeConst: 'Kinds', MigrationClass: 'AddKindsLookup', MigrationFile: '20261106090100_AddKindsLookup', MigrationId: '20261106090100_AddKindsLookup',
      TypeNameEn: 'Kinds', TypeNameAr: 'أنواع', IdConstants: '    public const int A = 1;', SeedRows: '        new(Id: KindIds.A, Code: "a", Names: new(Ar: "أ", En: "A"), SortOrder: 10),',
      ModuleRulePrefix: 'demo', CodeCases: '    [InlineData("a", KindIds.A)]', TypeKey: 'kinds',
      Usings: '', TestClass: 'EscalateFromOpenTransitionTests', DefinitionClass: 'DemoWorkflow', CatalogSource: 'DemoPermissionCatalogSource',
      StatusKey: 'open', StatusPascal: 'Open', StatusRef: 'DemoStatuses.Open', TerminalAssert: 'False', ExtraTests: '',
      ActionKey: 'escalate', FromKey: 'open', ToKey: 'escalated', ActionPascal: 'Escalate', FromPascal: 'Open', ToPascal: 'Escalated',
      ActionRef: 'DemoActions.Escalate', FromRef: 'DemoStatuses.Open', ToRef: 'DemoStatuses.Escalated', PermissionRef: 'DemoPermissionKeys.View',
      OwnerCall: '', ReasonArgument: '', TerminalCall: '', Side: 'Staff', IsOwner: 'false' };
    for (const set of templateSets()) {
      const files = renderTemplateSet(set, values);
      assert.ok(files.length > 0, set);
      for (const file of files) assert.doesNotMatch(file.content + file.path, /\{\{|__[A-Z]/, `${set}/${file.path}`);
    }
  });

  it('refuses a template token the values do not define', () => {
    assert.throws(() => renderTemplateSet('module', { Module: 'Demo' }), /Unknown template token/);
  });

  it('skips what is not there yet and honours the skip switch', () => {
    const lines = regenerate(root, { apiClient: true, env: {} });
    assert.equal(lines.length, 3);
    assert.ok(lines.every((line) => line.startsWith('skipped ')));
    assert.match(regenerate(root, { apiClient: true, env: { [SKIP_GENERATE_ENV]: '1' } })[0], /skipped downstream generators/);
  });

  it('resolves dotnet from CODELAB_DOTNET, then the private SDK, then PATH', () => {
    assert.equal(resolveDotnet({ CODELAB_DOTNET: 'X:\\dotnet.exe' }, () => true), 'X:\\dotnet.exe');
    assert.match(resolveDotnet({}, () => true), /codelab-tools/);
    assert.equal(resolveDotnet({}, () => false), 'dotnet');
  });
});
