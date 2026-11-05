import type { PermissionKey } from './permission-keys.generated';

/** How a list of permission keys is matched. Integers, like every enum the starter owns. */
export enum PermissionMatchMode {
  /** At least one key is held (the default). */
  Any = 1,
  /** Every key is held. */
  All = 2,
}

/** One permission key, or several matched by a {@link PermissionMatchMode}. */
export type PermissionRequirement = PermissionKey | readonly PermissionKey[];

/**
 * Whether `has` satisfies `required`. An empty list is never satisfied (fail closed: a gate that names no permission
 * shows nothing), whatever the mode.
 */
export function holdsPermissions(
  has: (key: string) => boolean,
  required: PermissionRequirement | null | undefined,
  mode: PermissionMatchMode = PermissionMatchMode.Any,
): boolean {
  if (required === null || required === undefined) {
    return false;
  }
  const keys: readonly string[] = typeof required === 'string' ? [required] : required;
  if (keys.length === 0) {
    return false;
  }
  return mode === PermissionMatchMode.All ? keys.every(has) : keys.some(has);
}
