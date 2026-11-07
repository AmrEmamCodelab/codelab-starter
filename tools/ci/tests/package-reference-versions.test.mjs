// Run: node --test "tools/ci/tests/**/*.test.mjs"
//
// Regression: tools/build.ps1 packs every .NET package with one global -p:PackageVersion, and that global property
// flows into the package's ProjectReferences. A package that references another Codelab package therefore records a
// dependency at its OWN version; when versions.json gives the two packages different versions, Test-NuspecDependencies
// refuses the pack (CI step 13, -Pack and -Release) and a hand-built release would depend on a version that does not
// exist. Codelab.Integration 0.5.0 -> Codelab.Security 0.6.0 broke exactly this way.
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { afterEach, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const REPO_ROOT = fileURLToPath(new URL('../../../', import.meta.url));
const VERSIONS_FILE = path.join('packages', 'versions.json');
const DOTNET_PACKAGES_DIR = path.join('packages', 'dotnet');
const PROJECT_EXTENSION = '.csproj';
const PROJECT_REFERENCE = /<ProjectReference\s+Include="([^"]+)"/g;
const NPM_SCOPE_PREFIX = '@';

/** The .NET package ids in versions.json (npm packages are scoped and have no csproj). */
function dotnetPackageIds(versions) {
  return Object.keys(versions).filter((id) => !id.startsWith(NPM_SCOPE_PREFIX));
}

/** The project names (file name without .csproj) a csproj references. */
function referencedProjects(csprojText) {
  return [...csprojText.matchAll(PROJECT_REFERENCE)].map((match) => {
    const include = match[1].replaceAll('\\', '/');
    return path.posix.basename(include, PROJECT_EXTENSION);
  });
}

/**
 * Every pair "<package> -> <referenced package>" whose versions.json versions differ. A reference to a project that
 * is not a versioned package (a build-only project) is ignored. A package without a csproj under packages/dotnet
 * (the SDK package) has no ProjectReferences to check.
 */
function referenceVersionMismatches(root) {
  const versions = JSON.parse(readFileSync(path.join(root, VERSIONS_FILE), 'utf8'));
  const mismatches = [];
  for (const id of dotnetPackageIds(versions)) {
    const csproj = path.join(root, DOTNET_PACKAGES_DIR, id, `${id}${PROJECT_EXTENSION}`);
    let text;
    try {
      text = readFileSync(csproj, 'utf8');
    } catch {
      continue;
    }
    for (const referenced of referencedProjects(text)) {
      if (Object.hasOwn(versions, referenced) && versions[referenced] !== versions[id]) {
        mismatches.push(`${id} ${versions[id]} -> ${referenced} ${versions[referenced]}`);
      }
    }
  }
  return mismatches;
}

const tempRoots = [];

function fixtureRepo(versions, csprojByPackage) {
  const root = mkdtempSync(path.join(tmpdir(), 'pkg-ref-versions-'));
  tempRoots.push(root);
  mkdirSync(path.join(root, 'packages'), { recursive: true });
  writeFileSync(path.join(root, VERSIONS_FILE), JSON.stringify(versions));
  for (const [id, text] of Object.entries(csprojByPackage)) {
    const dir = path.join(root, DOTNET_PACKAGES_DIR, id);
    mkdirSync(dir, { recursive: true });
    writeFileSync(path.join(dir, `${id}${PROJECT_EXTENSION}`), text);
  }
  return root;
}

function csprojReferencing(...names) {
  const items = names.map((name) => `<ProjectReference Include="..\\${name}\\${name}${PROJECT_EXTENSION}" />`);
  return `<Project><ItemGroup>${items.join('')}</ItemGroup></Project>`;
}

afterEach(() => {
  while (tempRoots.length > 0) {
    rmSync(tempRoots.pop(), { recursive: true, force: true });
  }
});

describe('package ProjectReference versions', () => {
  it('every Codelab package has the same versions.json version as the Codelab packages it references', () => {
    assert.deepEqual(referenceVersionMismatches(REPO_ROOT), []);
  });

  it('reports a package that references a package with another version', () => {
    const root = fixtureRepo(
      { 'Codelab.Alpha': '0.5.0', 'Codelab.Beta': '0.6.0', '@codelab/ng-gamma': '0.1.0' },
      { 'Codelab.Alpha': csprojReferencing('Codelab.Beta'), 'Codelab.Beta': csprojReferencing() },
    );
    assert.deepEqual(referenceVersionMismatches(root), ['Codelab.Alpha 0.5.0 -> Codelab.Beta 0.6.0']);
  });

  it('accepts equal versions and ignores references to projects that are not packages', () => {
    const root = fixtureRepo(
      { 'Codelab.Alpha': '0.6.0', 'Codelab.Beta': '0.6.0', 'Codelab.Sdk': '0.2.1' },
      { 'Codelab.Alpha': csprojReferencing('Codelab.Beta', 'Codelab.Analyzers'), 'Codelab.Beta': csprojReferencing() },
    );
    assert.deepEqual(referenceVersionMismatches(root), []);
  });

  it('reads forward-slash and backslash Include paths', () => {
    const text = '<ProjectReference Include="../A/Codelab.A.csproj" /><ProjectReference Include="..\\B\\Codelab.B.csproj" />';
    assert.deepEqual(referencedProjects(text), ['Codelab.A', 'Codelab.B']);
  });
});
