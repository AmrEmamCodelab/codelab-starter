// The three generators as plans over a ChangeSet: they validate, render and record every write; nothing touches the
// disk until the caller commits the set, so a refusal changes nothing.
import {
  API_PROJECT,
  ENDPOINT_SHAPES,
  ID_ROUTE_DOC,
  Markers,
  MODULE_TEMPLATE_SET,
  MODULE_TESTS_ROOT,
  MODULES_ROOT,
  ModuleFiles,
  SOLUTION_FILE,
  SOLUTION_TOKEN,
  VALIDATION_NAMESPACE,
} from './constants.mjs';
import { ScaffoldRefusal, addCatalogEntry, addRegistrations, addStringConstant, addUsings } from './csharp-edit.mjs';
import {
  permissionKeyOf,
  refusalForAction,
  refusalForMethod,
  refusalForModuleName,
  refusalForPascalName,
  refusalForPermissionKey,
  refusalForRoute,
  toConstantCase,
  toKebabCase,
  toPascalCase,
  toWords,
} from './names.mjs';
import { addModuleProjectReference, addSolutionProjects } from './project-files.mjs';
import { renderTemplateSet } from './templates.mjs';

function refuseIf(message) {
  if (message) throw new ScaffoldRefusal(message);
}

function moduleDir(module) {
  return `${MODULES_ROOT}/${module}`;
}

function moduleFile(module, file) {
  return `${moduleDir(module)}/${file}`;
}

function moduleValues(module) {
  return { Module: module, ModuleKebab: toKebabCase(module), ModuleUpper: toConstantCase(module) };
}

/** Writes rendered templates; a file that exists with other content is refused (no silent overwrite). */
function writeRendered(changes, files) {
  const clashes = files.filter((file) => changes.read(file.path) !== null && changes.read(file.path) !== file.content);
  refuseIf(clashes.length > 0 && `These files already exist with other content: ${clashes.map((file) => file.path).join(', ')}.`);
  for (const file of files) changes.write(file.path, file.content);
}

function requireModule(changes, module, existing) {
  refuseIf(refusalForPascalName(module, 'The module name'));
  refuseIf(!existing.includes(module) && `There is no module '${module}' (modules: ${existing.join(', ') || 'none'}).`);
  const keys = moduleFile(module, ModuleFiles.permissionKeys(module));
  refuseIf(changes.read(keys) === null && `The module '${module}' has no ${keys}.`);
}

/**
 * new-module: the module anatomy, its test project, the solution entries and the host ProjectReference.
 * @param {import('./workspace.mjs').ChangeSet} changes
 * @param {{ name: string, existingModules: string[], removedModules: string[] }} request
 */
export function planModule(changes, { name, existingModules, removedModules }) {
  refuseIf(refusalForModuleName(name, { existingModules, removedModules }));
  refuseIf(changes.exists(moduleDir(name)) && `The folder ${moduleDir(name)} already exists.`);
  writeRendered(changes, renderTemplateSet(MODULE_TEMPLATE_SET, moduleValues(name)));

  const projectPath = moduleFile(name, ModuleFiles.project(name));
  const testProjectPath = `${MODULE_TESTS_ROOT}/${SOLUTION_TOKEN}.Modules.${name}.Tests/${ModuleFiles.testProject(name)}`;
  const solution = changes.read(SOLUTION_FILE);
  refuseIf(solution === null && `${SOLUTION_FILE} is missing.`);
  changes.write(SOLUTION_FILE, addSolutionProjects(solution, [projectPath, testProjectPath]).text);
  const host = changes.read(API_PROJECT);
  refuseIf(host === null && `${API_PROJECT} is missing.`);
  const include = `..\\Modules\\${name}\\${ModuleFiles.project(name)}`;
  changes.write(API_PROJECT, addModuleProjectReference(host, include).text);
}

/**
 * new-permission: the key constant and its catalog entry. Idempotent: an existing identical key is a no-op.
 * @returns {{ constName: string, key: string }}
 */
export function planPermission(changes, { module, action, description, existingModules }) {
  requireModule(changes, module, existingModules);
  refuseIf(refusalForAction(action));
  const key = permissionKeyOf(module, action);
  refuseIf(refusalForPermissionKey(key));
  const constName = toPascalCase(action);
  const words = toWords(action);
  const text = description ?? `${words[0].toUpperCase()}${words.slice(1)} (${module} module).`;

  const keysPath = moduleFile(module, ModuleFiles.permissionKeys(module));
  const keys = addStringConstant(changes.read(keysPath), { constName, value: key, summary: text, marker: Markers.permissionKeys });
  const catalogPath = moduleFile(module, ModuleFiles.permissionCatalog(module));
  const catalogText = changes.read(catalogPath);
  refuseIf(catalogText === null && `The module '${module}' has no ${catalogPath}.`);
  const catalog = addCatalogEntry(catalogText, { keysClass: `${module}PermissionKeys`, constName, description: text });
  changes.write(keysPath, keys.text);
  changes.write(catalogPath, catalog.text);
  return { constName, key };
}

function endpointRegistrations(name, shape) {
  const statements = [`services.AddScoped<${name}>();`, `services.AddScoped<I${name}Store, ${name}Store>();`];
  if (shape.hasBody) statements.push(`services.AddScoped<IValidator<${name}Command>, ${name}Validator>();`);
  return statements;
}

/**
 * new-endpoint: the controller, the use case with its port, validator and refusal, the placeholder store, the tests,
 * the error code, the permission (when missing) and the DI registrations.
 * @returns {{ permission: { constName: string, key: string }, errorCode: string }}
 */
export function planEndpoint(changes, { module, name, method, route, permission, existingModules }) {
  requireModule(changes, module, existingModules);
  refuseIf(refusalForPascalName(name, 'The endpoint name'));
  refuseIf(refusalForMethod(method));
  refuseIf(refusalForRoute(route));
  refuseIf(name === module && `The endpoint name may not equal the module name '${module}'.`);
  const shape = ENDPOINT_SHAPES[method.toUpperCase()];

  const granted = planPermission(changes, { module, action: permission ?? shape.defaultAction, description: undefined, existingModules });
  const errorConst = `${name}${shape.errorSuffix}`;
  const errorCode = `${toConstantCase(module)}_${toConstantCase(name)}_${shape.errorCodeSuffix}`;
  const errorsPath = moduleFile(module, ModuleFiles.errorCodes(module));
  const errorsText = changes.read(errorsPath);
  refuseIf(errorsText === null && `The module '${module}' has no ${errorsPath}.`);
  const httpRoute = `${method.toUpperCase()} /api/${toKebabCase(module)}/${route}${shape.hasId ? `/${ID_ROUTE_DOC}` : ''}`;
  const summary = `${shape.errorSummary} (<c>${httpRoute}</c>).`;
  changes.write(errorsPath, addStringConstant(errorsText, { constName: errorConst, value: errorCode, summary, marker: Markers.errorCodes }).text);
  writeRendered(changes, renderTemplateSet(shape.templateSet, {
    ...moduleValues(module),
    Name: name,
    Route: route,
    PermissionConst: granted.constName,
    ErrorConst: errorConst,
    HttpRoute: httpRoute,
  }));

  const registrationPath = moduleFile(module, ModuleFiles.registration(module));
  const registration = changes.read(registrationPath);
  refuseIf(registration === null && `The module '${module}' has no ${registrationPath}.`);
  let text = addRegistrations(registration, endpointRegistrations(name, shape)).text;
  const namespaces = [`${SOLUTION_TOKEN}.Modules.${module}.Application`, ...(shape.hasBody ? [VALIDATION_NAMESPACE] : [])];
  text = addUsings(text, namespaces).text;
  changes.write(registrationPath, text);
  return { permission: granted, errorCode };
}
