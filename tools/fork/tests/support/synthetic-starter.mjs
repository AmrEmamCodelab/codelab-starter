// A synthetic starter repo in %TEMP%: Starter.* names, a tiny solution with modules A and B (B has tests and a
// frontend feature), a fake packages/ folder, a fake source feed outside the repo, stub rules compiler and scaffolder,
// and a copy of the real fork tools (so a renamed fork runs its own renamed tools, as a real fork does).
import { spawnSync } from 'node:child_process';
import { cpSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { testEnvironment } from './powershell.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
export const REPO_ROOT = join(HERE, '..', '..', '..', '..');
export const BRANCH = 'main';

/** Versions the synthetic packages/versions.json lists; the fake feed holds them (the SDK only as ci.3). */
export const VERSIONS = Object.freeze({ 'Codelab.Build.Sdk': '0.2.1', 'Codelab.Security': '0.5.1', '@codelab/ng-security': '0.5.1' });
export const FEED_FILES = Object.freeze({
  nuget: ['Codelab.Build.Sdk.0.2.1-ci.2.nupkg', 'Codelab.Build.Sdk.0.2.1-ci.3.nupkg', 'Codelab.Security.0.5.1.nupkg', 'Codelab.Security.0.5.0.nupkg'],
  npm: ['codelab-ng-security-0.5.1.tgz'],
});
export const EDIT_FILES = Object.freeze(['docs/one.md', 'docs/two.md', 'docs/three.md', 'docs/four.md', 'docs/five.md']);

const CRLF = '\r\n';
const crlf = (text) => text.split('\n').join(CRLF);

/** The fork tools copied into the synthetic starter (paths relative to the repo root). */
const FORK_TOOL_FILES = ['init.ps1', 'tools/remove-module.ps1', 'tools/upstream-merge.ps1', 'tools/fork/fork.mjs', 'tools/fork/Test-ForkModel.ps1', 'tools/fork/codelab.starter.schema.json'];
const FORK_TOOL_DIRS = ['tools/fork/lib'];

export function write(root, relativePath, content) {
  const full = join(root, relativePath);
  mkdirSync(dirname(full), { recursive: true });
  writeFileSync(full, content);
  return full;
}

export function read(root, relativePath) {
  return readFileSync(join(root, relativePath), 'utf8');
}

export function git(cwd, args) {
  const result = spawnSync('git', args, { cwd, encoding: 'utf8', env: testEnvironment(), windowsHide: true });
  if (result.status !== 0) throw new Error(`git ${args.join(' ')} failed in ${cwd}:\n${result.stderr}`);
  return result.stdout.trim();
}

export function commitAll(cwd, message) {
  git(cwd, ['add', '-A']);
  git(cwd, ['commit', '--quiet', '-m', message]);
  return git(cwd, ['rev-parse', 'HEAD']);
}

const SOLUTION = `<Solution>
  <Folder Name="/backend/">
    <Project Path="backend/src/Starter.Api/Starter.Api.csproj" />
    <Project Path="backend/src/Modules/A/Starter.Modules.A.csproj" />
    <Project Path="backend/src/Modules/B/Starter.Modules.B.csproj" />
    <Project Path="backend/tests/Modules/Starter.Modules.B.Tests/Starter.Modules.B.Tests.csproj" />
  </Folder>
  <Folder Name="/packages/">
    <Project Path="packages/dotnet/Codelab.Fake/Codelab.Fake.csproj" />
  </Folder>
</Solution>
`;

const API_PROJECT = `<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <RootNamespace>Starter.Api</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\\Modules\\A\\Starter.Modules.A.csproj" />
    <ProjectReference Include="..\\Modules\\B\\Starter.Modules.B.csproj" />
  </ItemGroup>
</Project>
`;

const BUILD_PROPS = `<Project>
  <PropertyGroup>
    <UseCodelabPackageSource Condition="'$(UseCodelabPackageSource)' == ''">true</UseCodelabPackageSource>
    <CodelabBuildSdkVersion Condition="'$(CodelabBuildSdkVersion)' == ''">0.2.1</CodelabBuildSdkVersion>
  </PropertyGroup>
</Project>
`;

const PACKAGES_PROPS = `<Project>
  <ItemGroup Label="Codelab">
    <PackageVersion Include="Codelab.Security" Version="0.0.0" />
  </ItemGroup>
</Project>
`;

/** A stub rules compiler: writes the sorted module folder names to rules/rules.compiled.json (deterministic). */
const RULES_COMPILER = `import { existsSync, mkdirSync, readdirSync, writeFileSync } from 'node:fs';
const dir = 'backend/src/Modules';
const modules = existsSync(dir) ? readdirSync(dir).sort() : [];
mkdirSync('rules', { recursive: true });
writeFileSync('rules/rules.compiled.json', JSON.stringify({ modules }, null, 2) + '\\n');
`;

/** A stub scaffolder: a module folder, its csproj and MODULE.md, plus its solution and host lines. */
const SCAFFOLDER = `import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
const name = process.argv[process.argv.indexOf('--name') + 1];
const dir = 'backend/src/Modules/' + name + '/';
mkdirSync(dir, { recursive: true });
writeFileSync(dir + 'Starter.Modules.' + name + '.csproj', '<Project Sdk="Microsoft.NET.Sdk" />\\r\\n');
writeFileSync(dir + 'MODULE.md', '# ' + name + '\\n\\nScaffolded module.\\n');
const slnx = readFileSync('codelab-starter.slnx', 'utf8');
writeFileSync('codelab-starter.slnx', slnx.replace('  </Folder>', '    <Project Path="' + dir + 'Starter.Modules.' + name + '.csproj" />\\r\\n  </Folder>'));
const api = 'backend/src/Starter.Api/Starter.Api.csproj';
const host = readFileSync(api, 'utf8');
writeFileSync(api, host.replace('  </ItemGroup>', '    <ProjectReference Include="..\\\\Modules\\\\' + name + '\\\\Starter.Modules.' + name + '.csproj" />\\r\\n  </ItemGroup>'));
`;

function writeManifest(root) {
  const manifest = JSON.parse(read(REPO_ROOT, 'codelab.starter.json'));
  write(root, 'codelab.starter.json', `${JSON.stringify(manifest, null, 2)}\n`);
}

function writeBackend(root) {
  write(root, 'codelab-starter.slnx', crlf(SOLUTION));
  write(root, 'backend/src/Starter.Api/Starter.Api.csproj', crlf(API_PROJECT));
  write(root, 'backend/src/Starter.Api/appsettings.json', `${JSON.stringify({ Codelab: { Security: { Protocol: { Prefix: 'STARTER' } } }, ConnectionStrings: { Starter: 'Database=StarterDev' } }, null, 2)}\n`);
  write(root, 'backend/src/Starter.Api/Program.cs', 'namespace Starter.Api;\n\npublic static class Program\n{\n    public const string Database = "StarterDev";\n    public const string Restarter = "Restarter";\n}\n');
  for (const name of ['A', 'B']) {
    write(root, `backend/src/Modules/${name}/Starter.Modules.${name}.csproj`, crlf('<Project Sdk="Microsoft.NET.Sdk">\n</Project>\n'));
    write(root, `backend/src/Modules/${name}/${name}Module.cs`, `namespace Starter.Modules.${name};\n\npublic sealed class ${name}Module\n{\n    public const string Key = "STARTER_${name}";\n}\n`);
    write(root, `backend/src/Modules/${name}/MODULE.md`, `# Module ${name}\n\n## Purpose\n\nSynthetic module ${name}.\n`);
  }
  write(root, 'backend/tests/Modules/Starter.Modules.B.Tests/Starter.Modules.B.Tests.csproj', crlf('<Project Sdk="Microsoft.NET.Sdk">\n</Project>\n'));
  write(root, 'backend/tests/Modules/Starter.Modules.B.Tests/BModuleTests.cs', 'namespace Starter.Modules.B.Tests;\n');
}

function writeFrontend(root) {
  write(root, 'package.json', `${JSON.stringify({ name: 'codelab-starter', private: true, workspaces: ['packages/npm/*', 'frontend/starter-web'] }, null, 2)}\n`);
  write(root, 'frontend/starter-web/package.json', `${JSON.stringify({ name: 'starter-web', private: true, dependencies: { '@codelab/ng-security': '0.5.1', tslib: '2.9.1' } }, null, 2)}\n`);
  write(root, 'frontend/starter-web/public/config.json', `${JSON.stringify({ apiBaseUrl: 'http://localhost:5180', security: { protocolPrefix: 'STARTER' }, defaultLanguage: 'ar' }, null, 2)}\n`);
  write(root, 'frontend/starter-web/src/app/features/b/b.page.ts', 'export const STARTER_B_PAGE = "b";\n');
  write(root, 'frontend/starter-web/src/app/features/a/a.page.ts', 'export const STARTER_A_PAGE = "a";\n');
}

function writePackages(root) {
  write(root, 'packages/versions.json', `${JSON.stringify(VERSIONS, null, 2)}\n`);
  write(root, 'packages/README.md', '# packages\n\nFake package source.\n');
  write(root, 'packages/dotnet/Codelab.Fake/Codelab.Fake.csproj', crlf('<Project Sdk="Microsoft.NET.Sdk">\n</Project>\n'));
}

function writeRepoFiles(root, options) {
  write(root, '.gitignore', '/feed/nuget/*\n/feed/npm/*\n!/feed/nuget/.gitkeep\n!/feed/npm/.gitkeep\nnode_modules/\n');
  write(root, '.gitattributes', '* text=auto\n*.ps1 text eol=crlf\n*.slnx text eol=crlf\n*.csproj text eol=crlf\n*.props text eol=crlf\n*.md text eol=lf\n*.json text eol=lf\n*.mjs text eol=lf\n*.cs text eol=lf\n');
  write(root, 'feed/nuget/.gitkeep', '');
  write(root, 'feed/npm/.gitkeep', '');
  write(root, 'Directory.Build.props', crlf(BUILD_PROPS));
  write(root, 'Directory.Packages.props', crlf(PACKAGES_PROPS));
  write(root, 'tools/rules-engine/compile.mjs', RULES_COMPILER);
  if (!options.withoutScaffolder) write(root, 'tools/scaffold/new-module.mjs', SCAFFOLDER);
  write(root, 'CONTEXT.md', '# Context\n\nThe Starter repo map.\n');
  EDIT_FILES.forEach((file, index) => write(root, file, `# Doc ${index + 1}\n\nLine one.\nLine two.\nLine three.\n`));
}

function copyForkTools(root) {
  for (const file of FORK_TOOL_FILES) write(root, file, readFileSync(join(REPO_ROOT, file)));
  for (const dir of FORK_TOOL_DIRS) cpSync(join(REPO_ROOT, dir), join(root, dir), { recursive: true });
}

/** The fake source feed (outside the repo), as init's -FeedDir. */
export function createFeed(feedDir) {
  for (const [folder, files] of Object.entries(FEED_FILES)) {
    for (const file of files) write(feedDir, `${folder}/${file}`, `fake ${file}`);
  }
  return feedDir;
}

/**
 * Creates the synthetic starter at `root` as a git repo on `main` with one commit; returns its HEAD.
 * @param {string} root
 * @param {{ withoutScaffolder?: boolean }} [options]
 */
export function createSyntheticStarter(root, options = {}) {
  writeManifest(root);
  writeBackend(root);
  writeFrontend(root);
  writePackages(root);
  writeRepoFiles(root, options);
  copyForkTools(root);
  spawnSync(process.execPath, ['tools/rules-engine/compile.mjs'], { cwd: root });
  git(root, ['init', '--quiet', '-b', BRANCH]);
  return commitAll(root, 'synthetic starter');
}

/** Clones the starter into `fork` (as a project would fork it). */
export function cloneFork(starter, fork) {
  git(dirname(fork), ['clone', '--quiet', '--branch', BRANCH, starter, fork]);
  return fork;
}
