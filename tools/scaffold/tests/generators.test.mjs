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
import { catalogCommands, regenerate, resolveDotnet } from '../lib/workspace.mjs';

const SKIP = '--skip-generate';
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
    assert.match(rules, /id: service-requests\.thin-endpoints/);
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
    assert.equal(lines.at(-1), 'frontend key: PermissionKeys.AccessAuditView ("access.audit-view")');
  });

  it('with --no-gen writes the C# catalog, runs nothing and prints the exact two regenerate commands', () => {
    const env = { CODELAB_DOTNET: 'C:/sdk/dotnet.exe' };
    const lines = newPermission(['--module', 'Access', '--action', 'audit-view', '--root', root, '--no-gen'], env);
    assert.deepEqual(lines, [
      'changed backend/src/Modules/Access/Application/AccessPermissionKeys.cs',
      'changed backend/src/Modules/Access/Application/AccessPermissionCatalogSource.cs',
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

describe('templates and downstream generators', () => {
  it('renders every template set without an unknown token', () => {
    const values = { Module: 'Demo', ModuleKebab: 'demo', ModuleUpper: 'DEMO', Name: 'GetWidget', Route: 'widgets', PermissionConst: 'View', ErrorConst: 'GetWidgetNotFound', HttpRoute: 'GET /api/demo/widgets/{id}' };
    for (const set of fs.readdirSync(TEMPLATES_ROOT)) {
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
