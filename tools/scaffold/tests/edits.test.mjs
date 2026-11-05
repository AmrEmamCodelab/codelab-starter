import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { Markers } from '../lib/constants.mjs';
import { ScaffoldRefusal, addCatalogEntry, addRegistrations, addStringConstant, addUsings } from '../lib/csharp-edit.mjs';
import { addModuleProjectReference, addSolutionProjects, referencedModules } from '../lib/project-files.mjs';

const SOLUTION = [
  '<Solution>',
  '  <Folder Name="/packages/">',
  '    <Project Path="packages/dotnet/A/A.csproj" />',
  '  </Folder>',
  '</Solution>',
  '',
].join('\n');

/** MSBuild Include paths use backslashes; built from parts so no literal reads like a share path. */
const msbuildPath = (...parts) => parts.join(String.fromCharCode(92));
const INFRASTRUCTURE_INCLUDE = msbuildPath('..', 'Starter.Infrastructure', 'Starter.Infrastructure.csproj');
const ACCESS_INCLUDE = msbuildPath('..', 'Modules', 'Access', 'Starter.Modules.Access.csproj');

const HOST = [
  '<Project Sdk="Microsoft.NET.Sdk.Web">',
  '  <ItemGroup>',
  `    <ProjectReference Include="${INFRASTRUCTURE_INCLUDE}" />`,
  '  </ItemGroup>',
  '',
  '  <ItemGroup Label="Modules">',
  `    <ProjectReference Include="${ACCESS_INCLUDE}" />`,
  '  </ItemGroup>',
  '</Project>',
  '',
].join('\n');

describe('solution edits', () => {
  it('creates the /backend/ folder when it is missing, then appends to it', () => {
    const first = addSolutionProjects(SOLUTION, ['backend/src/Modules/Demo/Starter.Modules.Demo.csproj']);
    assert.deepEqual(first.added, ['backend/src/Modules/Demo/Starter.Modules.Demo.csproj']);
    assert.match(first.text, /  <Folder Name="\/backend\/">\n    <Project Path="backend\/src\/Modules\/Demo\/Starter.Modules.Demo.csproj" \/>\n  <\/Folder>\n<\/Solution>/);
    const second = addSolutionProjects(first.text, ['backend/tests/Modules/Starter.Modules.Demo.Tests/Starter.Modules.Demo.Tests.csproj']);
    assert.equal((second.text.match(/<Folder Name="\/backend\/">/g) ?? []).length, 1);
    assert.match(second.text, /Demo.csproj" \/>\n    <Project Path="backend\/tests/);
  });

  it('is idempotent and compares paths case- and separator-insensitively', () => {
    const once = addSolutionProjects(SOLUTION, ['backend/src/X/X.csproj']).text;
    const twice = addSolutionProjects(once, [msbuildPath('backend', 'src', 'x', 'X.csproj')]);
    assert.equal(twice.text, once);
    assert.deepEqual(twice.added, []);
  });

  it('keeps CRLF line endings', () => {
    const result = addSolutionProjects(SOLUTION.replaceAll('\n', '\r\n'), ['backend/src/X/X.csproj']);
    assert.ok(!/[^\r]\n/.test(result.text));
  });

  it('refuses a file without </Solution>', () => {
    assert.throws(() => addSolutionProjects('<Nope />', ['a.csproj']), /<\/Solution>/);
  });
});

describe('host project edits', () => {
  const demo = msbuildPath('..', 'Modules', 'Demo', 'Starter.Modules.Demo.csproj');

  it('appends to the Modules ItemGroup and is idempotent', () => {
    const result = addModuleProjectReference(HOST, demo);
    assert.ok(result.added);
    assert.ok(result.text.includes(`Access.csproj" />\n    <ProjectReference Include="${demo}" />\n  </ItemGroup>`));
    assert.equal(addModuleProjectReference(result.text, demo).added, false);
    assert.deepEqual(referencedModules(result.text, 'Starter'), ['Access', 'Demo']);
  });

  it('creates the Modules ItemGroup when it is missing', () => {
    const bare = '<Project Sdk="Microsoft.NET.Sdk.Web">\n  <PropertyGroup />\n</Project>\n';
    const result = addModuleProjectReference(bare, demo);
    assert.match(result.text, /<ItemGroup Label="Modules">\n    <ProjectReference Include="[^"]+Demo.csproj" \/>\n  <\/ItemGroup>\n\n<\/Project>/);
  });
});

const KEYS = [
  'public static class DemoPermissionKeys',
  '{',
  '    public const string View = "demo.view";',
  '',
  `    ${Markers.permissionKeys}`,
  '}',
  '',
].join('\n');

const HAND_WRITTEN_KEYS = [
  'namespace X;',
  '',
  'public static class AccessPermissionKeys',
  '{',
  '    public const string DashboardView = "dashboard.view";',
  '}',
  '',
].join('\n');

describe('C# constant and catalog edits', () => {
  it('adds a constant above the marker, then is a no-op for the same value', () => {
    const request = { constName: 'Approve', value: 'demo.approve', summary: 'Approve.', marker: Markers.permissionKeys };
    const result = addStringConstant(KEYS, request);
    assert.ok(result.added);
    assert.match(result.text, /    \/\/\/ <summary>Approve.<\/summary>\n    public const string Approve = "demo.approve";\n\n    \/\/ scaffold:permission-keys/);
    assert.deepEqual(addStringConstant(result.text, request), { text: result.text, added: false });
  });

  it('refuses a constant that exists with another value, or a value under another name', () => {
    assert.throws(() => addStringConstant(KEYS, { constName: 'View', value: 'demo.other', summary: 's', marker: Markers.permissionKeys }), ScaffoldRefusal);
    assert.throws(() => addStringConstant(KEYS, { constName: 'Read', value: 'demo.view', summary: 's', marker: Markers.permissionKeys }), ScaffoldRefusal);
  });

  it('falls back to the class closing brace in a hand-written file', () => {
    const result = addStringConstant(HAND_WRITTEN_KEYS, { constName: 'AdminView', value: 'access.admin', summary: 'Admin.', marker: Markers.permissionKeys });
    assert.match(result.text, /"dashboard.view";\n\n    \/\/\/ <summary>Admin.<\/summary>\n    public const string AdminView = "access.admin";\n\}/);
  });

  it('adds a catalog entry above the marker or before "];", once', () => {
    const marked = `    [\n        new(Key: K.View, Description: "View."),\n        ${Markers.permissionCatalog}\n    ];\n`;
    const result = addCatalogEntry(marked, { keysClass: 'K', constName: 'Approve', description: 'Say "yes".' });
    assert.match(result.text, /        new\(Key: K.Approve, Description: "Say \\"yes\\"."\),\n        \/\/ scaffold:permission-catalog/);
    assert.equal(addCatalogEntry(result.text, { keysClass: 'K', constName: 'Approve', description: 'x' }).added, false);
    const bare = '    [\n        new(Key: K.View, Description: "View."),\n    ];\n';
    assert.match(addCatalogEntry(bare, { keysClass: 'K', constName: 'Approve', description: 'A.' }).text, /View."\),\n        new\(Key: K.Approve/);
  });

  it('refuses a catalog file without a collection expression', () => {
    assert.throws(() => addCatalogEntry('class C {}', { keysClass: 'K', constName: 'A', description: 'A.' }), ScaffoldRefusal);
  });
});

describe('registration and using edits', () => {
  const marked = `    {\n        services.AddX();\n        ${Markers.registrations}\n        return services;\n    }\n`;

  it('adds each registration once, above the marker', () => {
    const result = addRegistrations(marked, ['services.AddScoped<A>();', 'services.AddX();']);
    assert.deepEqual(result.added, ['services.AddScoped<A>();']);
    assert.match(result.text, /AddX\(\);\n        services.AddScoped<A>\(\);\n        \/\/ scaffold:registrations/);
    assert.deepEqual(addRegistrations(result.text, ['services.AddScoped<A>();']).added, []);
  });

  it('falls back to the line before "return services;"', () => {
    const bare = '    {\n        services.AddX();\n        return services;\n    }\n';
    assert.match(addRegistrations(bare, ['services.AddScoped<A>();']).text, /AddX\(\);\n        services.AddScoped<A>\(\);\n        return services;/);
  });

  it('adds missing usings sorted into the block, once', () => {
    const text = 'using Microsoft.Extensions.DependencyInjection;\nusing Starter.Modules.Demo.Application;\n\nnamespace X;\n';
    const result = addUsings(text, ['Starter.Application.Validation', 'Starter.Modules.Demo.Application']);
    assert.deepEqual(result.added, ['using Starter.Application.Validation;']);
    assert.equal(result.text, 'using Microsoft.Extensions.DependencyInjection;\nusing Starter.Application.Validation;\nusing Starter.Modules.Demo.Application;\n\nnamespace X;\n');
    assert.deepEqual(addUsings(result.text, ['Starter.Application.Validation']).added, []);
  });

  it('adds a using block to a file without one', () => {
    assert.equal(addUsings('namespace X;\n', ['A.B']).text, 'using A.B;\n\nnamespace X;\n');
  });
});
