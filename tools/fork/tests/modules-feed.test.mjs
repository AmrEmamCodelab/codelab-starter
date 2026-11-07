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
  isOwnedPath,
  normalizeOptionalModuleIds,
  OPTIONAL_MARKER_SCAN,
  OPTIONAL_MODULE_IDS,
  OPTIONAL_MODULES,
  optionalModuleOwnedPaths,
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

describe('optional modules (init Step 2)', () => {
  const REQUIRED_MODULES = ['Identity', 'Access'];
  const FILE_EXTENSION = /\.[a-z0-9]+$/i;

  it('lists exactly the four contract ids, in removal order', () => {
    assert.deepEqual(OPTIONAL_MODULE_IDS, ['attachments', 'portal', 'sms', 'ops']);
  });

  it('maps every id to its backend modules and marker name', () => {
    assert.deepEqual(OPTIONAL_MODULES.attachments.moduleNames, ['Attachments']);
    assert.deepEqual(OPTIONAL_MODULES.portal.moduleNames, ['Portal']);
    assert.deepEqual(OPTIONAL_MODULES.sms.moduleNames, []);
    assert.deepEqual(OPTIONAL_MODULES.ops.moduleNames, ['Ops']);
    assert.deepEqual(OPTIONAL_MODULE_IDS.map((id) => OPTIONAL_MODULES[id].markerName), ['Attachments', 'Portal', 'Sms', 'Ops']);
  });

  it('never lists a required module or a kernel project', () => {
    for (const id of OPTIONAL_MODULE_IDS) {
      for (const name of OPTIONAL_MODULES[id].moduleNames) assert.equal(refusalForModuleName(name, REQUIRED_MODULES), null, `${id}: ${name}`);
    }
  });

  it('owns the services, deploy-kit scripts and tests of each id', () => {
    assert.ok(optionalModuleOwnedPaths('attachments').includes('services/attachment-service/'));
    assert.ok(optionalModuleOwnedPaths('attachments').includes('deploy/kits/Install-Attachment.ps1'));
    assert.ok(optionalModuleOwnedPaths('attachments').includes('frontend/starter-web/src/app/features/attachments/'));
    assert.ok(optionalModuleOwnedPaths('portal').includes('frontend/starter-web/src/app/features/portal/'));
    assert.ok(optionalModuleOwnedPaths('portal').includes('frontend/starter-web/src/app/features/admin/portal-content/'));
    assert.ok(optionalModuleOwnedPaths('sms').includes('services/integration-gateway/src/Codelab.Gateway.Api/Sms/'));
    assert.ok(!optionalModuleOwnedPaths('sms').some((owned) => owned === 'services/integration-gateway/'), 'the gateway stays for SSO');
    assert.ok(optionalModuleOwnedPaths('ops').includes('services/ops-console/'));
    assert.ok(optionalModuleOwnedPaths('ops').includes('deploy/kits/Install-Ops.ps1'));
    assert.ok(optionalModuleOwnedPaths('ops').includes('backend/src/Modules/Ops/'));
    assert.deepEqual(OPTIONAL_MODULES.ops.npmWorkspaces, ['services/ops-console/web/ops-web']);
  });

  it('keeps every path repo-relative and posix: folders end with /, files have an extension, no duplicates', () => {
    const all = OPTIONAL_MODULE_IDS.flatMap(optionalModuleOwnedPaths);
    assert.equal(new Set(all).size, all.length);
    for (const owned of [...all, ...OPTIONAL_MODULE_IDS.flatMap((id) => OPTIONAL_MODULES[id].markerAnchors)]) {
      assert.ok(!owned.startsWith('/') && !owned.includes('\\') && !owned.includes('..'), owned);
      assert.ok(owned.endsWith('/') || FILE_EXTENSION.test(owned), owned);
    }
  });

  it('never makes a marker anchor one of its own owned paths', () => {
    for (const id of OPTIONAL_MODULE_IDS) {
      const owned = optionalModuleOwnedPaths(id);
      for (const anchor of OPTIONAL_MODULES[id].markerAnchors) assert.ok(!isOwnedPath(anchor, owned), `${id}: ${anchor}`);
    }
  });

  it('scans PowerShell and C# files too, with # as a comment opener', () => {
    assert.ok(OPTIONAL_MARKER_SCAN.extensions.includes('.cs') && OPTIONAL_MARKER_SCAN.extensions.includes('.ps1'));
    assert.ok(OPTIONAL_MARKER_SCAN.commentOpeners.includes('#'));
    assert.ok(OPTIONAL_MARKER_SCAN.skippedDirs.includes('node_modules'));
  });

  it('normalizes ids: case, spaces, duplicates and contract order', () => {
    assert.deepEqual(normalizeOptionalModuleIds([' Ops', 'ATTACHMENTS', 'ops']), { ids: ['attachments', 'ops'], refusal: null });
    assert.deepEqual(normalizeOptionalModuleIds([]), { ids: [], refusal: null });
  });

  it('refuses unknown ids, required modules and a non-list', () => {
    assert.match(normalizeOptionalModuleIds(['identity']).refusal, /'identity' is not an optional module id/);
    assert.match(normalizeOptionalModuleIds(['ops', 42]).refusal, /'42' is not an optional module id/);
    assert.match(normalizeOptionalModuleIds('ops').refusal, /must be a list/);
  });

  it('matches owned folders by prefix and owned files exactly', () => {
    const owned = ['services/ops-console/', 'deploy/kits/Install-Ops.ps1'];
    assert.ok(isOwnedPath('services/ops-console/src/A.csproj', owned));
    assert.ok(isOwnedPath('services\\ops-console\\web\\x.ts', owned));
    assert.ok(!isOwnedPath('services/ops-console', owned));
    assert.ok(isOwnedPath('deploy/kits/Install-Ops.ps1', owned));
    assert.ok(!isOwnedPath('deploy/kits/Install-Ops.ps1.bak', owned));
    assert.ok(!isOwnedPath('services/ops-console-two/x', owned));
  });
});
