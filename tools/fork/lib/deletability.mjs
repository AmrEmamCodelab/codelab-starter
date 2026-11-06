// The module-deletability proof (tools/ci/module-deletability.ps1). Two commands of fork.mjs, both run in a temp
// copy of the repo, never in the repo itself:
//   add-probe       registers a scaffolded module the way a real one is registered: a frontend route behind module
//                   markers in app.routes.ts, a marked block in the host project and `<camelName>.` translation keys.
//   verify-removed  lists every remnant of a module after remove-module (folders, solution and host lines, marker
//                   lines, translation keys, the removedModules record); none means the removal was complete.
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { API_PROJECT, DELETABILITY_PROBE, GIT_METADATA, I18N_FILES, JSON_INDENT, MANIFEST_FILE, MODULE_MARKER, SOLUTION_FILE } from './constants.mjs';
import { readManifest } from './manifest.mjs';
import { hasModuleMarkers, removeI18nPrefix, toCamelCase } from './module-markers.mjs';
import { modulePaths, referencesModuleProject, removeProjectReferences, removeSolutionProjects, toKebabCase } from './modules.mjs';
import { listMarkerCandidates } from './remove-module.mjs';
import { RefusalError } from './repo.mjs';

const LINE_BREAK = '\n';
const CRLF = '\r\n';
const KEBAB_SEPARATOR = /-/g;
const SNAKE_SEPARATOR = '_';

/** The names the probe derives from a module name. */
export function probeNames(name) {
  const kebab = toKebabCase(name);
  return {
    kebab,
    camel: toCamelCase(name),
    routesConst: `${kebab.replace(KEBAB_SEPARATOR, SNAKE_SEPARATOR).toUpperCase()}${DELETABILITY_PROBE.routesConstSuffix}`,
    routesFile: `${modulePaths(name).frontend}${kebab}${DELETABILITY_PROBE.routesFileSuffix}`,
  };
}

const eolOf = (text) => (text.includes(CRLF) ? CRLF : LINE_BREAK);

/** The probe feature's routes file: an empty route list the app routes spread. */
export function probeRoutesFile(name) {
  const { routesConst } = probeNames(name);
  return [
    "import type { Routes } from '@angular/router';",
    '',
    `/** Deletability probe of module ${name} (tools/ci/module-deletability.ps1): removed with the module. */`,
    `export const ${routesConst}: Routes = [];`,
    '',
  ].join(LINE_BREAK);
}

/**
 * Registers the probe in app.routes.ts: a marked import as the first line and a marked block with the route spread
 * under DELETABILITY_PROBE.routesAnchor.
 * @throws {RefusalError} when the anchor line is missing
 */
export function addProbeRoute(text, name) {
  const { kebab, routesConst } = probeNames(name);
  const eol = eolOf(text);
  const lines = text.split(eol);
  const anchor = lines.findIndex((line) => line.trim() === DELETABILITY_PROBE.routesAnchor);
  if (anchor < 0) throw new RefusalError(`${DELETABILITY_PROBE.appRoutesFile} has no line '${DELETABILITY_PROBE.routesAnchor}' to register the probe under.`);
  const block = [
    `  // ${MODULE_MARKER.begin} ${name}`,
    `  ...${routesConst},`,
    `  // ${MODULE_MARKER.end} ${name}`,
  ];
  lines.splice(anchor + 1, 0, ...block);
  const importLine = `import { ${routesConst} } from './features/${kebab}/${kebab}${DELETABILITY_PROBE.routesImportSuffix}'; // ${MODULE_MARKER.line} ${name}`;
  return [importLine, ...lines].join(eol);
}

/** Adds a marked, empty, labelled ItemGroup block before the host project's closing tag. */
export function addProbeHostBlock(text, name) {
  const close = text.lastIndexOf(DELETABILITY_PROBE.projectClose);
  if (close < 0) throw new RefusalError(`${API_PROJECT} has no ${DELETABILITY_PROBE.projectClose}.`);
  const eol = eolOf(text);
  const block = [
    `  <!-- ${MODULE_MARKER.begin} ${name} -->`,
    `  <ItemGroup Label="${name} ${DELETABILITY_PROBE.itemGroupLabel}" />`,
    `  <!-- ${MODULE_MARKER.end} ${name} -->`,
    '',
  ].join(eol);
  return `${text.slice(0, close)}${block}${text.slice(close)}`;
}

/** Adds the probe's `<camelName>.probe` key (last, keeping the file's two-space format and final line break). */
export function addProbeTranslation(text, name, value) {
  const parsed = JSON.parse(text);
  parsed[probeNames(name).camel] = { [DELETABILITY_PROBE.translationKey]: value };
  const finalBreak = text.endsWith(LINE_BREAK) ? LINE_BREAK : '';
  return `${JSON.stringify(parsed, null, JSON_INDENT)}${finalBreak}`;
}

function translationFor(file) {
  const language = DELETABILITY_PROBE.i18nLanguageFile.exec(file)?.[1];
  return DELETABILITY_PROBE.translation[language] ?? DELETABILITY_PROBE.translation.en;
}

function planEdit(root, file, transform) {
  const absolute = path.join(root, file);
  if (!existsSync(absolute)) throw new RefusalError(`${file} not found; the probe registers itself there.`);
  return { absolute, text: transform(readFileSync(absolute, 'utf8')) };
}

/**
 * Registers the probe of a scaffolded module in the repo at `root`, a temp copy: plans every edit first, then
 * writes. Refuses a root with git metadata (the real repo or a clone), a module whose backend folder does not exist
 * (scaffold it first) and an existing frontend feature folder.
 */
export function addProbe(root, name, io) {
  if (existsSync(path.join(root, GIT_METADATA))) throw new RefusalError(`${root} has ${GIT_METADATA}; the probe runs only in a temp copy without git metadata.`);
  const names = probeNames(name);
  const paths = modulePaths(name);
  if (!existsSync(path.join(root, paths.backend))) throw new RefusalError(`${paths.backend} not found; scaffold the module first (tools/scaffold/new-module.mjs).`);
  if (existsSync(path.join(root, paths.frontend))) throw new RefusalError(`${paths.frontend} already exists; the probe adds its own feature folder.`);
  const edits = [
    planEdit(root, DELETABILITY_PROBE.appRoutesFile, (text) => addProbeRoute(text, name)),
    planEdit(root, API_PROJECT, (text) => addProbeHostBlock(text, name)),
    ...I18N_FILES.map((file) => planEdit(root, file, (text) => addProbeTranslation(text, name, translationFor(file)))),
  ];
  mkdirSync(path.join(root, paths.frontend), { recursive: true });
  writeFileSync(path.join(root, names.routesFile), probeRoutesFile(name));
  for (const edit of edits) writeFileSync(edit.absolute, edit.text);
  io.log(`Probe of module ${name} registered: ${names.routesFile}, ${DELETABILITY_PROBE.appRoutesFile}, ${API_PROJECT}, ${I18N_FILES.join(', ')}.`);
}

function readIfExists(root, file) {
  const absolute = path.join(root, file);
  return existsSync(absolute) ? readFileSync(absolute, 'utf8') : null;
}

/**
 * Every remnant of module `name` in the repo at `root`, as readable lines; empty when the removal was complete.
 * @returns {string[]}
 */
export function findRemnants(root, name) {
  const remnants = [];
  for (const folder of Object.values(modulePaths(name))) {
    if (existsSync(path.join(root, folder))) remnants.push(`folder ${folder} still exists`);
  }
  const solution = readIfExists(root, SOLUTION_FILE);
  if (solution !== null) {
    for (const project of removeSolutionProjects(solution, (projectPath) => referencesModuleProject(projectPath, name)).removed) remnants.push(`${SOLUTION_FILE} still lists ${project}`);
  }
  const host = readIfExists(root, API_PROJECT);
  if (host !== null) {
    for (const reference of removeProjectReferences(host, (include) => referencesModuleProject(include, name)).removed) remnants.push(`${API_PROJECT} still references ${reference}`);
  }
  for (const file of listMarkerCandidates(root, [])) {
    if (hasModuleMarkers(readFileSync(path.join(root, file), 'utf8'), name)) remnants.push(`${file} still has ${MODULE_MARKER.line} ${name} markers`);
  }
  for (const file of I18N_FILES) {
    const text = readIfExists(root, file);
    if (text === null) continue;
    const { removedKeys } = removeI18nPrefix(text, toCamelCase(name), file);
    if (removedKeys.length > 0) remnants.push(`${file} still has key(s) ${removedKeys.join(', ')}`);
  }
  if (!readManifest(root).removedModules.includes(name)) remnants.push(`${MANIFEST_FILE} removedModules does not record ${name}`);
  return remnants;
}
