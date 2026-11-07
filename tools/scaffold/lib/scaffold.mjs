// The module, endpoint and permission generators as plans over a ChangeSet: they validate, render and record every write; nothing touches the
// disk until the caller commits the set, so a refusal changes nothing.
import {
  API_PROJECT,
  API_ROUTE_PREFIX,
  ENDPOINT_SHAPES,
  ID_ROUTE_DOC,
  Markers,
  MODULE_TEMPLATE_SET,
  MODULE_TESTS_ROOT,
  MODULES_ROOT,
  MODULE_ROOT_ROUTE,
  ModuleFiles,
  Recipes,
  ROUTE_SEPARATOR,
  SOLUTION_FILE,
  SOLUTION_TOKEN,
  VALIDATION_NAMESPACE,
} from './constants.mjs';
import { ScaffoldRefusal, addCatalogEntry, addRegistrations, addStringConstant, addUsings } from './csharp-edit.mjs';
import {
  parseRoute,
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
import { documentModule } from './module-docs.mjs';
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

/** The rules engine's rule-id prefix of a module: its lower-cased name (ServiceRequests -> servicerequests). */
export function moduleRulePrefix(module) {
  return module.toLowerCase();
}

function moduleValues(module) {
  return { Module: module, ModuleKebab: toKebabCase(module), ModuleUpper: toConstantCase(module), ModuleRulePrefix: moduleRulePrefix(module) };
}

/** Records the MODULE.md note and the recipe of a change to `module`; returns the report notes (files missing). */
function documentChange(changes, module, lines, recipe) {
  return documentModule(changes, {
    docPath: moduleFile(module, ModuleFiles.doc),
    rulesPath: moduleFile(module, ModuleFiles.rules),
    lines,
    recipe,
  });
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
 * new-permission: the key constant, its catalog entry, the MODULE.md note and the add-permission recipe in the
 * module's rules. Idempotent: an existing identical key is a no-op (and gets no note).
 * @returns {{ constName: string, key: string, added: boolean, notes: string[] }}
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
  const added = keys.text !== changes.read(keysPath);
  changes.write(keysPath, keys.text);
  changes.write(catalogPath, catalog.text);
  const note = `- Permission \`${key}\` (\`${module}PermissionKeys.${constName}\`): ${text} ${DocNotes.permission}`;
  const notes = added ? documentChange(changes, module, [note], Recipes.permission) : [];
  return { constName, key, added, notes };
}

/** The tail of each MODULE.md note: which generator wrote the line. */
const DocNotes = Object.freeze({
  permission: 'Added with tools/scaffold/new-permission.mjs.',
  endpoint: 'added with tools/scaffold/new-endpoint.mjs',
});

function endpointRegistrations(name, shape) {
  const statements = [`services.AddScoped<${name}>();`, `services.AddScoped<I${name}Store, ${name}Store>();`];
  if (shape.hasBody) statements.push(`services.AddScoped<IValidator<${name}Command>, ${name}Validator>();`);
  return statements;
}

/** The using line ApiRoutes needs (unnecessary usings fail the build, IDE0005). */
const HOSTING_USING = 'using Starter.Infrastructure.Hosting;\n';

/** Joins the non-empty route parts with '/'. */
function joinRoute(...parts) {
  return parts.filter((part) => part !== '').join(ROUTE_SEPARATOR);
}

/**
 * The C# route expressions and the documented route of an endpoint: the controller route (the module root, plus the
 * part of --route before the id), the action template (the module's IdTemplate, plus the part after the id) and the
 * HTTP route as it reads in docs. A route that names the id is refused for a POST, which creates the record.
 */
export function endpointRoutes(module, method, shape, parsed) {
  refuseIf(parsed.explicitId && !shape.hasId
    && `A ${method.toUpperCase()} creates a record, so its route cannot hold ${ID_ROUTE_DOC}; give the collection route or '${MODULE_ROOT_ROUTE}'.`);
  const routesClass = `${module}Routes`;
  const quoted = (text) => `ApiRoutes.Separator + "${text}"`;
  const controllerRoute = parsed.prefix === '' ? `${routesClass}.Root` : `${routesClass}.Root + ${quoted(parsed.prefix)}`;
  const idTemplate = parsed.suffix === '' ? `${routesClass}.IdTemplate` : `${routesClass}.IdTemplate + ${quoted(parsed.suffix)}`;
  const controllerRouteText = joinRoute(API_ROUTE_PREFIX, toKebabCase(module), parsed.prefix);
  const documented = joinRoute(controllerRouteText, shape.hasId ? ID_ROUTE_DOC : '', shape.hasId ? parsed.suffix : '');
  const usesApiRoutes = parsed.prefix !== '' || (shape.hasId && parsed.suffix !== '');
  return { controllerRoute, controllerRouteText, idTemplate, usesApiRoutes, httpRoute: `${method.toUpperCase()} /${documented}` };
}

/**
 * new-endpoint: the controller, the use case with its port, validator and refusal, the placeholder store, the tests,
 * the error code, the permission (when missing) and the DI registrations, then the MODULE.md note and the add-endpoint
 * recipe in the module's rules.
 * @returns {{ permission: { constName: string, key: string }, errorCode: string, notes: string[] }}
 */
export function planEndpoint(changes, { module, name, method, route, permission, existingModules }) {
  requireModule(changes, module, existingModules);
  refuseIf(refusalForPascalName(name, 'The endpoint name'));
  refuseIf(refusalForMethod(method));
  refuseIf(refusalForRoute(route));
  refuseIf(name === module && `The endpoint name may not equal the module name '${module}'.`);
  const shape = ENDPOINT_SHAPES[method.toUpperCase()];
  const routes = endpointRoutes(module, method, shape, parseRoute(route));

  const granted = planPermission(changes, { module, action: permission ?? shape.defaultAction, description: undefined, existingModules });
  const errorConst = `${name}${shape.errorSuffix}`;
  const errorCode = `${toConstantCase(module)}_${toConstantCase(name)}_${shape.errorCodeSuffix}`;
  const errorsPath = moduleFile(module, ModuleFiles.errorCodes(module));
  const errorsText = changes.read(errorsPath);
  refuseIf(errorsText === null && `The module '${module}' has no ${errorsPath}.`);
  const httpRoute = routes.httpRoute;
  const summary = `${shape.errorSummary} (<c>${httpRoute}</c>).`;
  changes.write(errorsPath, addStringConstant(errorsText, { constName: errorConst, value: errorCode, summary, marker: Markers.errorCodes }).text);
  writeRendered(changes, renderTemplateSet(shape.templateSet, {
    ...moduleValues(module),
    Name: name,
    ControllerRoute: routes.controllerRoute,
    ControllerRouteText: routes.controllerRouteText,
    IdTemplate: routes.idTemplate,
    HostingUsing: routes.usesApiRoutes ? HOSTING_USING : '',
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
  const note = `- Endpoint \`${httpRoute}\` (\`${name}\`, permission \`${granted.key}\`, refusal \`${errorCode}\`), ${DocNotes.endpoint}; tests \`${name}Tests.cs\`.`;
  const notes = [...granted.notes, ...documentChange(changes, module, [note], Recipes.endpoint)];
  return { permission: granted, errorCode, notes };
}
