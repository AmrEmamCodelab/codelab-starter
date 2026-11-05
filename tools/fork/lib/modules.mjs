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
