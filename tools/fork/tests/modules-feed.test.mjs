// Unit tests of the module anatomy and the feed-mode edits. Run: node --test "tools/fork/tests/**/*.test.mjs"
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import {
  feedFileName,
  kindOf,
  PackageKind,
  pinPackageVersions,
  resolveFeedPackages,
  resolveFeedVersion,
  rewriteNpmManifest,
  setFeedModeBuildProps,
} from '../lib/feed.mjs';
import {
  createRemovedPathMatcher,
  createRemovedProjectLineMatcher,
  modulePaths,
  moduleProjectFiles,
  referencesModuleProject,
  refusalForModuleName,
  removeMatchingLines,
  removeProjectReferences,
  removeSolutionProjects,
  toKebabCase,
} from '../lib/modules.mjs';

describe('module anatomy', () => {
  it('derives kebab-case feature names', () => {
    assert.equal(toKebabCase('ServiceRequests'), 'service-requests');
    assert.equal(toKebabCase('Demo'), 'demo');
    assert.equal(toKebabCase('HTTPGateway'), 'http-gateway');
    assert.equal(toKebabCase('Access2Fa'), 'access2-fa');
  });

  it('lists the module folders and project files', () => {
    assert.deepEqual(modulePaths('ServiceRequests'), {
      backend: 'backend/src/Modules/ServiceRequests/',
      tests: 'backend/tests/Modules/Starter.Modules.ServiceRequests.Tests/',
      frontend: 'frontend/starter-web/src/app/features/service-requests/',
    });
    assert.deepEqual(moduleProjectFiles('Demo'), ['Starter.Modules.Demo.csproj', 'Starter.Modules.Demo.Tests.csproj']);
  });

  it('refuses invalid names, kernel projects and required modules (any case)', () => {
    assert.equal(refusalForModuleName('Demo', ['Identity']), null);
    assert.match(refusalForModuleName('demo', []), /not a module name/);
    assert.match(refusalForModuleName('Starter.Api', []), /not a module name/);
    assert.match(refusalForModuleName('Domain', []), /kernel project Starter\.Domain/);
    assert.match(refusalForModuleName('API', []), /not a module name|kernel/);
    assert.match(refusalForModuleName('Identity', ['Identity', 'Access']), /required module/);
  });

  it('matches removed areas: packages/ and every folder of each removed module', () => {
    const isRemoved = createRemovedPathMatcher(['B']);
    for (const path of ['packages/versions.json', 'backend/src/Modules/B/x.cs', 'backend/tests/Modules/Starter.Modules.B.Tests/t.cs', 'frontend/starter-web/src/app/features/b/b.ts', 'backend\\src\\Modules\\B\\y.cs']) {
      assert.equal(isRemoved(path), true, path);
    }
    for (const path of ['backend/src/Modules/BB/x.cs', 'backend/src/Modules/A/x.cs', 'docs/packages/x.md', 'frontend/starter-web/src/app/features/bb/x.ts']) {
      assert.equal(isRemoved(path), false, path);
    }
  });

  it('matches only the lines that name a removed module project', () => {
    assert.equal(createRemovedProjectLineMatcher([]), null);
    const matches = createRemovedProjectLineMatcher(['B']);
    assert.equal(matches('    <Project Path="backend/src/Modules/B/Starter.Modules.B.csproj" />\r'), true);
    assert.equal(matches('    <ProjectReference Include="..\\Modules\\B\\Starter.Modules.B.csproj" />'), true);
    assert.equal(matches('    <Project Path="backend/tests/Modules/Starter.Modules.B.Tests/Starter.Modules.B.Tests.csproj" />'), true);
    assert.equal(matches('    <Project Path="backend/src/Modules/BB/Starter.Modules.BB.csproj" />'), false);
    assert.equal(matches('    <Project Path="backend/src/Modules/A/Starter.Modules.A.csproj" />'), false);
    assert.equal(removeMatchingLines('a\nStarter.Modules.B.csproj"\nc\n', matches), 'a\nc\n');
    assert.equal(removeMatchingLines('a\n', null), 'a\n');
  });

  it('removes solution projects (self-closing and with children) and the folders they empty', () => {
    const slnx = [
      '<Solution>',
      '  <Folder Name="/backend/">',
      '    <Project Path="backend/src/Modules/A/Starter.Modules.A.csproj" />',
      '    <Project Path="backend/src/Modules/B/Starter.Modules.B.csproj">',
      '      <Build Project="false" />',
      '    </Project>',
      '  </Folder>',
      '  <Folder Name="/packages/">',
      '    <Project Path="packages/dotnet/X/X.csproj" />',
      '  </Folder>',
      '</Solution>',
      '',
    ].join('\r\n');
    const result = removeSolutionProjects(slnx, (path) => path.startsWith('packages/') || referencesModuleProject(path, 'B'));
    assert.deepEqual(result.removed, ['backend/src/Modules/B/Starter.Modules.B.csproj', 'packages/dotnet/X/X.csproj']);
    assert.equal(result.text, ['<Solution>', '  <Folder Name="/backend/">', '    <Project Path="backend/src/Modules/A/Starter.Modules.A.csproj" />', '  </Folder>', '</Solution>', ''].join('\r\n'));
    assert.equal(removeSolutionProjects(slnx, () => false).text, slnx);
  });

  it('removes host project references and the item groups they empty', () => {
    const csproj = '<Project>\n  <ItemGroup>\n    <ProjectReference Include="..\\Modules\\B\\Starter.Modules.B.csproj" />\n  </ItemGroup>\n</Project>\n';
    const result = removeProjectReferences(csproj, (include) => referencesModuleProject(include, 'B'));
    assert.deepEqual(result.removed, ['../Modules/B/Starter.Modules.B.csproj']);
    assert.equal(result.text, '<Project>\n</Project>\n');
  });
});

describe('feed versions', () => {
  it('names feed files', () => {
    assert.equal(kindOf('@codelab/ng-dga'), PackageKind.npm);
    assert.equal(feedFileName('Codelab.Security', '0.5.1'), 'Codelab.Security.0.5.1.nupkg');
    assert.equal(feedFileName('@codelab/ng-security', '0.5.1-ci.3'), 'codelab-ng-security-0.5.1-ci.3.tgz');
  });

  it('prefers the exact version, else the highest ci prerelease of that version only', () => {
    const files = ['Codelab.Security.0.5.0-ci.99.nupkg', 'codelab.security.0.5.1-ci.2.nupkg', 'Codelab.Security.0.5.1-ci.10.nupkg', 'Codelab.Security.0.5.1-ci.9.nupkg'];
    assert.equal(resolveFeedVersion('Codelab.Security', '0.5.1', files), '0.5.1-ci.10');
    assert.equal(resolveFeedVersion('Codelab.Security', '0.5.1', [...files, 'Codelab.Security.0.5.1.nupkg']), '0.5.1');
    assert.equal(resolveFeedVersion('Codelab.Security', '0.6.0', files), null);
    assert.equal(resolveFeedVersion('@codelab/ng-security', '0.5.1', ['codelab-ng-security-0.5.1-ci.4.tgz']), '0.5.1-ci.4');
  });

  it('resolves a versions.json against a listing and lists what is missing', () => {
    const { resolved, missing } = resolveFeedPackages(
      { 'Codelab.Build.Sdk': '0.2.1', 'Codelab.Errors': '0.5.1', '@codelab/ng-tools': '0.1.1' },
      { nuget: ['Codelab.Build.Sdk.0.2.1.nupkg'], npm: ['codelab-ng-tools-0.1.1.tgz'] },
    );
    assert.deepEqual(resolved, [
      { id: 'Codelab.Build.Sdk', version: '0.2.1', file: 'Codelab.Build.Sdk.0.2.1.nupkg', folder: 'nuget' },
      { id: '@codelab/ng-tools', version: '0.1.1', file: 'codelab-ng-tools-0.1.1.tgz', folder: 'npm' },
    ]);
    assert.deepEqual(missing, ['Codelab.Errors 0.5.1']);
  });
});

describe('feed-mode edits', () => {
  const props = '<Project>\r\n  <PropertyGroup>\r\n    <UseCodelabPackageSource Condition="x">true</UseCodelabPackageSource>\r\n    <CodelabBuildSdkVersion Condition="y">0.2.1</CodelabBuildSdkVersion>\r\n  </PropertyGroup>\r\n</Project>\r\n';

  it('switches Directory.Build.props to feed mode with the pinned SDK', () => {
    const text = setFeedModeBuildProps(props, '0.2.1-ci.3');
    assert.match(text, /<UseCodelabPackageSource Condition="x">false<\/UseCodelabPackageSource>/);
    assert.match(text, /<CodelabBuildSdkVersion Condition="y">0\.2\.1-ci\.3<\/CodelabBuildSdkVersion>/);
    assert.throws(() => setFeedModeBuildProps('<Project />', '1.0.0'), /no <UseCodelabPackageSource>/);
  });

  it('pins existing Codelab PackageVersions and adds the missing ones (never the SDK or npm ids)', () => {
    const text = '<Project>\r\n  <ItemGroup>\r\n    <PackageVersion Include="Codelab.Security" Version="$(CodelabSecurityVersion)" />\r\n    <PackageVersion Version="1.0.0" Include="Codelab.Errors" />\r\n  </ItemGroup>\r\n</Project>\r\n';
    const pinned = pinPackageVersions(text, { 'Codelab.Build.Sdk': '0.2.1', 'Codelab.Security': '0.5.1', 'Codelab.Errors': '0.5.1-ci.2', 'Codelab.Testing': '0.5.0', '@codelab/ng-tools': '0.1.1' });
    assert.match(pinned, /Include="Codelab\.Security" Version="0\.5\.1"/);
    assert.match(pinned, /Version="0\.5\.1-ci\.2" Include="Codelab\.Errors"/);
    assert.match(pinned, /<ItemGroup Label="Codelab packages \(feed only, pinned by init\.ps1\)">\r\n    <PackageVersion Include="Codelab\.Testing" Version="0\.5\.0" \/>\r\n  <\/ItemGroup>\r\n<\/Project>\r\n$/);
    assert.doesNotMatch(pinned, /Codelab\.Build\.Sdk|ng-tools/);
    assert.equal(pinPackageVersions(text, {}), text);
  });

  it('points @codelab dependencies at feed tarballs and drops packages/ workspaces', () => {
    const manifest = '{\n  "name": "starter-web",\n  "workspaces": ["packages/npm/*", "frontend/starter-web"],\n  "dependencies": { "@codelab/ng-security": "0.5.1", "rxjs": "7.8.2" },\n  "devDependencies": { "@codelab/ng-tools": "0.1.1" }\n}\n';
    const result = rewriteNpmManifest(manifest, { '@codelab/ng-security': 'codelab-ng-security-0.5.1.tgz', '@codelab/ng-tools': 'codelab-ng-tools-0.1.1.tgz' }, '../../feed/npm');
    const parsed = JSON.parse(result.text);
    assert.equal(parsed.dependencies['@codelab/ng-security'], 'file:../../feed/npm/codelab-ng-security-0.5.1.tgz');
    assert.equal(parsed.devDependencies['@codelab/ng-tools'], 'file:../../feed/npm/codelab-ng-tools-0.1.1.tgz');
    assert.equal(parsed.dependencies.rxjs, '7.8.2');
    assert.deepEqual(parsed.workspaces, ['frontend/starter-web']);
    assert.deepEqual(result.droppedWorkspaces, ['packages/npm/*']);
    assert.ok(result.text.endsWith('}\n'));
    assert.throws(() => rewriteNpmManifest(manifest, {}, 'feed/npm'), /not in packages\/versions\.json/);
  });
});
