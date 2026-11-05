// Name conversion and validation for the scaffold generators. Pure functions; every refusal is a message string.
import {
  HttpMethod,
  KEBAB_NAME_PATTERN,
  KERNEL_PROJECTS,
  MAX_NAME_LENGTH,
  PASCAL_NAME_PATTERN,
  PERMISSION_KEY_MAX_LENGTH,
  PERMISSION_KEY_PATTERN,
  RESERVED_NAMES,
  ROUTE_SEGMENT_PATTERN,
  SOLUTION_TOKEN,
} from './constants.mjs';

const WORD_BOUNDARY = /(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])/g;
const KEBAB_SEPARATOR = '-';
const SNAKE_SEPARATOR = '_';
/** Separates the segments of a dotted permission key. */
const PERMISSION_KEY_SEGMENT_SEPARATOR = '.';

/** "ServiceRequests" -> "service-requests". */
export function toKebabCase(name) {
  return name.replace(WORD_BOUNDARY, KEBAB_SEPARATOR).toLowerCase();
}

/** "approve-request" -> "ApproveRequest". */
export function toPascalCase(kebab) {
  return kebab
    .split(KEBAB_SEPARATOR)
    .filter((part) => part.length > 0)
    .map((part) => part[0].toUpperCase() + part.slice(1))
    .join('');
}

/** "ServiceRequests" -> "SERVICE_REQUESTS" (error code prefixes). */
export function toConstantCase(name) {
  return name.replace(WORD_BOUNDARY, SNAKE_SEPARATOR).toUpperCase();
}

/** "approve-request" -> "approve request" (generated descriptions). */
export function toWords(kebab) {
  return kebab.split(KEBAB_SEPARATOR).join(' ');
}

/** Why `name` is not a PascalCase name, or null. `label` names the argument in the message. */
export function refusalForPascalName(name, label) {
  if (typeof name !== 'string' || name.length === 0) return `${label} is required.`;
  if (name.length > MAX_NAME_LENGTH) return `${label} '${name}' is longer than ${MAX_NAME_LENGTH} characters.`;
  if (!PASCAL_NAME_PATTERN.test(name)) {
    return `${label} '${name}' is not PascalCase (letters and digits, each word capitalized, e.g. ServiceRequests).`;
  }
  return null;
}

/**
 * Why a new module cannot be called `name`, or null.
 * @param {string} name
 * @param {{ existingModules: string[], removedModules: string[] }} context
 */
export function refusalForModuleName(name, { existingModules, removedModules }) {
  const shape = refusalForPascalName(name, 'The module name');
  if (shape) return shape;
  const same = (other) => other.toLowerCase() === name.toLowerCase() || toKebabCase(other) === toKebabCase(name);
  const kernel = KERNEL_PROJECTS.find(same);
  if (kernel) return `'${name}' is the kernel project ${SOLUTION_TOKEN}.${kernel}; pick a business name.`;
  const reserved = RESERVED_NAMES.find(same);
  if (reserved) return `'${name}' is a reserved name (${RESERVED_NAMES.join(', ')}).`;
  const existing = existingModules.find(same);
  if (existing) return `A module named '${existing}' already exists.`;
  const removed = removedModules.find(same);
  if (removed) {
    return `'${removed}' is listed in codelab.starter.json removedModules; a new module with that name would collide with the upstream module on every merge. Pick another name.`;
  }
  return null;
}

/** Why `action` is not a kebab-case permission action, or null. */
export function refusalForAction(action) {
  if (typeof action !== 'string' || action.length === 0) return 'The permission action is required (kebab-case, e.g. approve or approve-request).';
  if (!KEBAB_NAME_PATTERN.test(action)) return `The permission action '${action}' is not kebab-case (e.g. approve-request).`;
  return null;
}

/** Why `key` breaks PermissionKeyFormat, or null. */
export function refusalForPermissionKey(key) {
  if (key.length > PERMISSION_KEY_MAX_LENGTH) return `The permission key '${key}' is longer than ${PERMISSION_KEY_MAX_LENGTH} characters.`;
  if (!PERMISSION_KEY_PATTERN.test(key)) return `The permission key '${key}' does not match the permission key format.`;
  return null;
}

/**
 * The frontend constant name api:gen gives a permission key (PascalCase of every dotted and dashed word):
 * "demo.approve-request" -> "DemoApproveRequest", read as PermissionKeys.DemoApproveRequest.
 */
export function frontendPermissionKeyName(key) {
  return key.split(PERMISSION_KEY_SEGMENT_SEPARATOR).map(toPascalCase).join('');
}

/** The permission key of a module action: Demo + approve-request -> "demo.approve-request". */
export function permissionKeyOf(moduleName, action) {
  return `${toKebabCase(moduleName)}.${action}`;
}

/** Why `method` is not a supported HTTP method, or null. */
export function refusalForMethod(method) {
  const known = Object.values(HttpMethod);
  if (typeof method !== 'string' || !known.includes(method.toUpperCase())) {
    return `The method '${method}' is not one of ${known.join(', ')}.`;
  }
  return null;
}

/** Why `route` is not a relative route segment (lower-case kebab words separated by '/'), or null. */
export function refusalForRoute(route) {
  if (typeof route !== 'string' || !ROUTE_SEGMENT_PATTERN.test(route)) {
    return `The route '${route}' must be relative kebab-case segments (e.g. widgets or widgets/archive), without the api prefix, slashes at the ends or parameters.`;
  }
  return null;
}
