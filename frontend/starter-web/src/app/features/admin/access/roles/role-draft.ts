import type { CatalogPermission, RoleDetail, UpdateRoleBody } from '../data/access.models';

/** The role editor's form: the two names and the selected permission keys. */
export interface RoleDraft {
  readonly nameEn: string;
  readonly nameAr: string;
  readonly permissions: ReadonlySet<string>;
}

/** The catalog split into the keys that can be granted (live) and the ones that left the code (retired). */
export interface CatalogSplit {
  /** Grantable keys, sorted: the tree shows these. */
  readonly live: readonly string[];
  readonly liveSet: ReadonlySet<string>;
  readonly retired: ReadonlySet<string>;
}

export function splitCatalog(catalog: readonly CatalogPermission[]): CatalogSplit {
  const live = catalog.filter((entry) => !entry.isRetired).map((entry) => entry.key).sort();
  const retired = new Set(catalog.filter((entry) => entry.isRetired).map((entry) => entry.key));
  return { live, liveSet: new Set(live), retired };
}

/** True when both sets hold the same keys. */
export function sameKeys(left: ReadonlySet<string>, right: ReadonlySet<string>): boolean {
  return left.size === right.size && [...left].every((key) => right.has(key));
}

/** The role's grantable keys: what the tree can show as selected. */
export function liveRoleKeys(role: RoleDetail, liveKeys: ReadonlySet<string>): ReadonlySet<string> {
  return new Set(role.permissions.filter((key) => liveKeys.has(key)));
}

/**
 * Keys the role still lists that are no longer grantable (retired, or unknown to the catalog), sorted. They grant
 * nothing; the editor shows them disabled and a save drops them (the server refuses a retired key).
 */
export function heldRetiredKeys(role: RoleDetail, liveKeys: ReadonlySet<string>): readonly string[] {
  return role.permissions.filter((key) => !liveKeys.has(key)).sort();
}

export function draftFromRole(role: RoleDetail, liveKeys: ReadonlySet<string>): RoleDraft {
  return { nameEn: role.nameEn, nameAr: role.nameAr, permissions: liveRoleKeys(role, liveKeys) };
}

/**
 * True when the draft differs from the saved role: a name (ignoring surrounding spaces) or the grantable keys.
 * Retired keys alone never make the role dirty, so the leave guard does not ask about them.
 */
export function isRoleDraftDirty(role: RoleDetail, draft: RoleDraft, liveKeys: ReadonlySet<string>): boolean {
  return draft.nameEn.trim() !== role.nameEn
    || draft.nameAr.trim() !== role.nameAr
    || !sameKeys(draft.permissions, liveRoleKeys(role, liveKeys));
}

/** The PUT body: trimmed names, the selected grantable keys sorted, the role's active state unchanged. */
export function roleUpdateBody(
  role: RoleDetail,
  draft: RoleDraft,
  liveKeys: ReadonlySet<string>,
  confirmBreakGlassOnly: boolean,
): UpdateRoleBody {
  return {
    nameEn: draft.nameEn.trim(),
    nameAr: draft.nameAr.trim(),
    permissions: [...draft.permissions].filter((key) => liveKeys.has(key)).sort(),
    isActive: role.isActive,
    confirmBreakGlassOnly,
  };
}
