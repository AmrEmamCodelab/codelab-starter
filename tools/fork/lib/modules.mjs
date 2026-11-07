// Module anatomy (ADR-004) as the fork tools see it: where a module's files live, and how it is listed in the
// solution and in the host project's ProjectReference list. Pure functions over paths and text.
import {
  KEBAB_PLACEHOLDER,
  KERNEL_PROJECTS,
  MODULE_NAME_PATTERN,
  MODULE_PATHS,
  MODULE_PROJECT_FILE,
  MODULE_TEST_PROJECT_FILE,
  NAME_PLACEHOLDER,
  PACKAGES_DIR,
  SOLUTION_TOKEN,
} from './constants.mjs';

const KEBAB_BOUNDARY = /(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])/g;
const REGEX_SPECIAL = /[.*+?^${}()|[\]\\]/g;
const BACKSLASH = /\\/g;
const SOLUTION_PROJECT_ELEMENT = /^[ \t]*<Project\s+Path="([^"]+)"[^>]*?(?:\/>|>[\s\S]*?<\/Project>)[ \t]*\r?\n/gm;
const PROJECT_REFERENCE_ELEMENT = /^[ \t]*<ProjectReference\s+Include="([^"]+)"[^>]*?(?:\/>|>[\s\S]*?<\/ProjectReference>)[ \t]*\r?\n/gm;
const EMPTIED_FOLDER = /^[ \t]*<Folder\s+Name="[^"]*"\s*>\s*<\/Folder>[ \t]*\r?\n/gm;
const EMPTIED_ITEM_GROUP = /^[ \t]*<ItemGroup\b[^>]*>\s*<\/ItemGroup>[ \t]*\r?\n/gm;

/** "ServiceRequests" -> "service-requests" (the frontend feature folder name). */
export function toKebabCase(name) {
  return name.replace(KEBAB_BOUNDARY, '-').toLowerCase();
}

function fill(template, name) {
  return template.replaceAll(NAME_PLACEHOLDER, name).replaceAll(KEBAB_PLACEHOLDER, toKebabCase(name));
}

/** The module's folders (repo-relative, '/' separators, trailing '/'). */
export function modulePaths(name) {
  return {
    backend: fill(MODULE_PATHS.backend, name),
    tests: fill(MODULE_PATHS.tests, name),
    frontend: fill(MODULE_PATHS.frontend, name),
  };
}

/** The module's project file names (its code and its tests). */
export function moduleProjectFiles(name) {
  return [fill(MODULE_PROJECT_FILE, name), fill(MODULE_TEST_PROJECT_FILE, name)];
}

/** Why a module name cannot be removed, or null when it can. */
export function refusalForModuleName(name, requiredModules) {
  if (typeof name !== 'string' || !MODULE_NAME_PATTERN.test(name)) {
    return `'${name}' is not a module name (PascalCase letters and digits, e.g. ServiceRequests).`;
  }
  const kernel = KERNEL_PROJECTS.find((project) => project.toLowerCase() === name.toLowerCase());
  if (kernel) return `'${name}' is the kernel project ${SOLUTION_TOKEN}.${kernel}; only modules can be removed.`;
  const required = requiredModules.find((module) => module.toLowerCase() === name.toLowerCase());
  if (required) return `'${required}' is a required module (codelab.starter.json requiredModules) and cannot be removed.`;
  return null;
}

/** Normalizes a path to '/' separators. */
export function toPosix(path) {
  return path.replace(BACKSLASH, '/');
}

/**
 * Builds the predicate "this repo-relative path belongs to a removed area": packages/ and every removed module's
 * folders. Paths are compared in the fork's naming (rename upstream paths before asking).
 * @param {string[]} removedModules
 */
export function createRemovedPathMatcher(removedModules) {
  const prefixes = [PACKAGES_DIR, ...removedModules.flatMap((name) => Object.values(modulePaths(name)))];
  return (path) => {
    const normalized = toPosix(path);
    return prefixes.some((prefix) => normalized.startsWith(prefix));
  };
}

function escapeRegex(value) {
  return value.replace(REGEX_SPECIAL, '\\$&');
}

/**
 * Builds the predicate "this line references a removed module's project" (its csproj or its tests' csproj), used
 * on the solution and host project lines. Returns null when no module is removed.
 * @param {string[]} removedModules
 */
export function createRemovedProjectLineMatcher(removedModules) {
  if (removedModules.length === 0) return null;
  const files = removedModules.flatMap(moduleProjectFiles).map(escapeRegex);
  const pattern = new RegExp(`(?:^|[\\\\/"])(?:${files.join('|')})(?=["\\s/>]|$)`, 'i');
  return (line) => pattern.test(line);
}

/** Removes the lines of `text` the predicate matches (line endings of the other lines are kept). */
export function removeMatchingLines(text, predicate) {
  if (!predicate) return text;
  return text.split('\n').filter((line) => !predicate(line)).join('\n');
}

function removeElements(text, pattern, shouldRemove) {
  const removed = [];
  const result = text.replace(pattern, (element, path) => {
    if (!shouldRemove(toPosix(path))) return element;
    removed.push(toPosix(path));
    return '';
  });
  return { text: result, removed };
}

/**
 * Removes every solution <Project> whose path satisfies the predicate, then any <Folder> left empty by that.
 * @param {string} text the .slnx content
 * @param {(path: string) => boolean} shouldRemove gets the repo-relative project path ('/' separators)
 */
export function removeSolutionProjects(text, shouldRemove) {
  const result = removeElements(text, SOLUTION_PROJECT_ELEMENT, shouldRemove);
  if (result.removed.length === 0) return result;
  return { text: result.text.replace(EMPTIED_FOLDER, ''), removed: result.removed };
}

/**
 * Removes every <ProjectReference> whose Include satisfies the predicate, then any <ItemGroup> left empty by that.
 * @param {string} text the .csproj content
 * @param {(include: string) => boolean} shouldRemove gets the Include value ('/' separators)
 */
export function removeProjectReferences(text, shouldRemove) {
  const result = removeElements(text, PROJECT_REFERENCE_ELEMENT, shouldRemove);
  if (result.removed.length === 0) return result;
  return { text: result.text.replace(EMPTIED_ITEM_GROUP, ''), removed: result.removed };
}

/** True when a project path or Include names one of the module's project files. */
export function referencesModuleProject(path, name) {
  const file = toPosix(path).split('/').pop().toLowerCase();
  return moduleProjectFiles(name).some((candidate) => candidate.toLowerCase() === file);
}

/**
 * The optional modules init Step 2 removes at file level (ADR-004), by lower-case id. Required modules (Identity,
 * Access) are never listed. Every entry says what the id owns:
 *   moduleNames   backend modules removed with removeModule (folders, .slnx, host reference, markers, i18n keys);
 *   markerName    the name its registrations in shared files carry (`codelab:module <markerName>`), also in .cs,
 *                 .ps1 and .psm1 files and outside the module folders (OPTIONAL_MARKER_SCAN);
 *   paths         further folders (trailing '/') and files it owns; their .slnx projects go with them;
 *   markerAnchors shared files that must carry its markers before it can be removed (a removal without them would
 *                 leave a build that does not compile, so it is refused, nothing changed); an id with none of its
 *                 paths left is already removed and needs none;
 *   npmWorkspaces root package.json workspaces it owns.
 * RENAME SAFETY: the literals are full paths, never built from parts, so a fork's renameMap rewrites them.
 */
export const OPTIONAL_MODULES = Object.freeze({
  attachments: Object.freeze({
    moduleNames: Object.freeze(['Attachments']),
    markerName: 'Attachments',
    paths: Object.freeze([
      'services/attachment-service/',
      'deploy/kits/Install-Attachment.ps1',
      'deploy/kits/templates/attachment/',
      'deploy/kits/tests/Install-Attachment.Tests.ps1',
      'deploy/kits/tests/fixtures/attachment/',
      'backend/tests/Starter.Integration.Tests/Attachments/',
      'backend/tests/Starter.Contract.Tests/Attachments/',
      'backend/tests/Modules/Starter.Modules.ServiceRequests.Tests/Attachments/',
      'backend/tests/Modules/Starter.Modules.ServiceRequests.Tests/Http/AttachmentTypeSeedHttpTests.cs',
      'frontend/starter-web/e2e/tests/attachment-checks.test.mjs',
    ]),
    markerAnchors: Object.freeze([
      'frontend/starter-web/src/app/app.routes.ts',
      'frontend/starter-web/src/app/app.spec.ts',
      'backend/tests/Starter.Integration.Tests/Records/ProbeRecord.cs',
      'backend/tests/Starter.Integration.Tests/Records/RecordsHost.cs',
      'backend/tests/Starter.Integration.Tests/Records/RecordsHttpTests.cs',
      'backend/tests/Starter.Integration.Tests/MigrationSquash/FreshDatabase.cs',
      'backend/tests/Starter.Integration.Tests/MigrationSquash/FreshDatabaseSeedTests.cs',
    ]),
    npmWorkspaces: Object.freeze([]),
  }),
  portal: Object.freeze({
    moduleNames: Object.freeze(['Portal']),
    markerName: 'Portal',
    paths: Object.freeze([
      'backend/tests/Starter.Integration.Tests/Portal/',
      'frontend/starter-web/src/app/features/admin/portal-content/',
    ]),
    markerAnchors: Object.freeze([
      'backend/tests/Starter.Integration.Tests/Support/Phase5Host.cs',
      'frontend/starter-web/src/app/app.routes.ts',
      'frontend/starter-web/src/app/public-routes.ts',
      'frontend/starter-web/src/app/features/admin/admin.routes.ts',
      'frontend/starter-web/src/app/features/admin/admin.paths.ts',
      'frontend/starter-web/src/app/features/admin/admin-page/admin-tiles.ts',
    ]),
    npmWorkspaces: Object.freeze([]),
  }),
  sms: Object.freeze({
    moduleNames: Object.freeze([]),
    markerName: 'Sms',
    paths: Object.freeze([
      'services/integration-gateway/src/Codelab.Gateway.Api/Sms/',
      'services/integration-gateway/tests/Codelab.Gateway.Api.Tests/Http/SmsRelayTests.cs',
      'services/integration-gateway/tests/Codelab.Gateway.Api.Tests/Unit/SmsUnitTests.cs',
      'backend/src/Modules/Notifications/Infrastructure/Sending/SmsRelayFailures.cs',
      'backend/tests/Modules/Starter.Modules.Notifications.Tests/Infrastructure/GatewaySmsSenderTests.cs',
      'backend/tests/Modules/Starter.Modules.Notifications.Tests/Infrastructure/GatewaySmsRelayIntegrationTests.cs',
    ]),
    markerAnchors: Object.freeze([
      'services/integration-gateway/src/Codelab.Gateway.Api/Hosting/GatewayHostExtensions.cs',
      'services/integration-gateway/tests/Codelab.Gateway.Api.Tests/TestSupport/GatewayHost.cs',
      'services/integration-gateway/tests/Codelab.Gateway.Api.Tests/TestSupport/FakeUpstream.cs',
      'services/integration-gateway/tests/Codelab.Gateway.Api.Tests/Hosting/DeployKitTemplateTests.cs',
      'services/integration-gateway/tests/Codelab.Gateway.Api.Tests/Http/ProfileSelectionTests.cs',
      'services/integration-gateway/tests/Codelab.Gateway.Api.Tests/Http/RelayClientContractTests.cs',
      'backend/src/Modules/Notifications/Infrastructure/NotificationsModuleRegistration.cs',
      'backend/src/Modules/Notifications/Infrastructure/Sending/SmsSenders.cs',
      'backend/tests/Modules/Starter.Modules.Notifications.Tests/NotificationsModuleTests.cs',
    ]),
    npmWorkspaces: Object.freeze([]),
  }),
  ops: Object.freeze({
    moduleNames: Object.freeze(['Ops']),
    markerName: 'Ops',
    paths: Object.freeze([
      'services/ops-console/',
      'deploy/kits/Install-Ops.ps1',
      'deploy/kits/templates/ops/',
      'deploy/kits/tests/Install-Ops.Tests.ps1',
    ]),
    markerAnchors: Object.freeze(['backend/tests/Starter.Contract.Tests/EndpointAllowlist.cs']),
    npmWorkspaces: Object.freeze(['services/ops-console/web/ops-web']),
  }),
});

/** The optional module ids, in removal order. */
export const OPTIONAL_MODULE_IDS = Object.freeze(Object.keys(OPTIONAL_MODULES));

/**
 * Where removeOptionalModules looks for an optional module's markers: wider than remove-module's own scan, because
 * an optional module also owns services, tests and deploy scripts. PowerShell files carry `# codelab:module <Name>`.
 */
export const OPTIONAL_MARKER_SCAN = Object.freeze({
  roots: Object.freeze(['backend/', 'services/', 'frontend/starter-web/src/', 'deploy/']),
  extensions: Object.freeze(['.cs', '.csproj', '.ts', '.html', '.ps1', '.psm1']),
  skippedDirs: Object.freeze(['node_modules', 'bin', 'obj', 'dist', '.angular']),
  commentOpeners: Object.freeze(['//', '<!--', '#']),
});

/**
 * Validates and normalizes optional module ids: lower-cased, de-duplicated, in OPTIONAL_MODULE_IDS order.
 * @param {unknown} ids
 * @returns {{ ids: string[], refusal: string | null }}
 */
export function normalizeOptionalModuleIds(ids) {
  if (!Array.isArray(ids)) return { ids: [], refusal: 'The optional module ids must be a list.' };
  const requested = new Set();
  for (const id of ids) {
    const normalized = typeof id === 'string' ? id.trim().toLowerCase() : '';
    if (!OPTIONAL_MODULE_IDS.includes(normalized)) {
      return { ids: [], refusal: `'${id}' is not an optional module id (one of ${OPTIONAL_MODULE_IDS.join(', ')}); required modules are never removed.` };
    }
    requested.add(normalized);
  }
  return { ids: OPTIONAL_MODULE_IDS.filter((id) => requested.has(id)), refusal: null };
}

/**
 * Everything an optional module id owns as repo-relative paths: its backend modules' folders and its own paths.
 * @param {string} id a valid optional module id
 */
export function optionalModuleOwnedPaths(id) {
  const entry = OPTIONAL_MODULES[id];
  return [...entry.moduleNames.flatMap((name) => Object.values(modulePaths(name))), ...entry.paths];
}

/** True when the repo-relative path is one of `ownedPaths` or lies inside one of its folders. */
export function isOwnedPath(path, ownedPaths) {
  const normalized = toPosix(path);
  return ownedPaths.some((owned) => (owned.endsWith('/') ? normalized.startsWith(owned) : normalized === owned));
}
