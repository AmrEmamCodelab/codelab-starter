import type { BilingualName } from '../common/access-labels';
import {
  DEFAULT_GRANT_SCOPE,
  toPermissionScope,
  type PermissionScope,
  type PutUserGrantsBody,
  type RoleId,
  type RoleSummary,
  type UserGrants,
} from '../data/access.models';

/** The grant editor's form: the assigned roles and the direct permissions, each with its scope. */
export interface GrantsDraft {
  readonly roles: ReadonlyMap<RoleId, PermissionScope>;
  readonly permissions: ReadonlyMap<string, PermissionScope>;
}

/** A role the editor can tick: an active ordinary role, or one the user already holds (even if inactive). */
export interface RoleOption extends BilingualName {
  readonly roleId: RoleId;
  readonly code: string;
  readonly isActive: boolean;
}

/** One effective permission with its sources resolved to role names. */
export interface EffectiveRow {
  readonly key: string;
  readonly scope: PermissionScope;
  readonly sources: readonly EffectiveSource[];
}

export interface EffectiveSource {
  readonly kind: number;
  readonly scope: PermissionScope;
  /** The role's names for a role or virtual-role source; null for a direct or break-glass source. */
  readonly role: BilingualName | null;
}

export function draftFromGrants(grants: UserGrants): GrantsDraft {
  return {
    roles: new Map(grants.roles.map((role) => [role.roleId, toPermissionScope(role.scope)])),
    permissions: new Map(grants.permissions.map((grant) => [grant.key, toPermissionScope(grant.scope)])),
  };
}

/** True when both maps hold the same ids with the same scopes. */
export function sameScopes<K>(left: ReadonlyMap<K, PermissionScope>, right: ReadonlyMap<K, PermissionScope>): boolean {
  return left.size === right.size && [...left].every(([id, scope]) => right.get(id) === scope);
}

export function isGrantsDraftDirty(grants: UserGrants, draft: GrantsDraft): boolean {
  const saved = draftFromGrants(grants);
  return !sameScopes(saved.roles, draft.roles) || !sameScopes(saved.permissions, draft.permissions);
}

/** The PUT body: the complete role and direct-grant sets (the write replaces both), sorted for a stable payload. */
export function putGrantsBody(draft: GrantsDraft, confirmBreakGlassOnly: boolean): PutUserGrantsBody {
  return {
    roles: [...draft.roles].sort(byId).map(([roleId, scope]) => ({ roleId, scope })),
    permissions: [...draft.permissions].sort(byId).map(([key, scope]) => ({ key, scope })),
    confirmBreakGlassOnly,
  };
}

function byId<K extends string>([left]: readonly [K, PermissionScope], [right]: readonly [K, PermissionScope]): number {
  return left.localeCompare(right);
}

/** Adds the role at `scope` (or keeps its current scope) when `assigned`, else removes it. */
export function withRole(
  roles: ReadonlyMap<RoleId, PermissionScope>,
  roleId: RoleId,
  assigned: boolean,
  scope: PermissionScope = DEFAULT_GRANT_SCOPE,
): ReadonlyMap<RoleId, PermissionScope> {
  const next = new Map(roles);
  if (!assigned) {
    next.delete(roleId);
  } else if (!next.has(roleId)) {
    next.set(roleId, scope);
  }
  return next;
}

/** Changes the scope of an entry already in the map; an absent id is left absent. */
export function withScope<K>(map: ReadonlyMap<K, PermissionScope>, id: K, scope: PermissionScope): ReadonlyMap<K, PermissionScope> {
  if (!map.has(id)) {
    return map;
  }
  return new Map(map).set(id, scope);
}

/**
 * The direct grants after the tree's selection changed to `keys`: kept keys keep their scope, new keys get
 * `scope`, unselected keys are removed.
 */
export function withDirectSelection(
  permissions: ReadonlyMap<string, PermissionScope>,
  keys: ReadonlySet<string>,
  scope: PermissionScope = DEFAULT_GRANT_SCOPE,
): ReadonlyMap<string, PermissionScope> {
  return new Map([...keys].sort().map((key) => [key, permissions.get(key) ?? scope]));
}

/**
 * The roles the editor offers: every active ordinary role of the list (null when the viewer may not list roles),
 * plus the roles the user already holds, so an inactive one can still be removed. Sorted by code.
 */
export function roleOptions(allRoles: readonly RoleSummary[] | null, grants: UserGrants): readonly RoleOption[] {
  const options = new Map<RoleId, RoleOption>();
  for (const role of allRoles ?? []) {
    if (role.isActive && !role.isVirtual) {
      options.set(role.id, { roleId: role.id, code: role.code, nameEn: role.nameEn, nameAr: role.nameAr, isActive: true });
    }
  }
  for (const held of grants.roles) {
    options.set(held.roleId, { roleId: held.roleId, code: held.code, nameEn: held.nameEn, nameAr: held.nameAr, isActive: held.isActive });
  }
  return [...options.values()].sort((left, right) => left.code.localeCompare(right.code));
}

/**
 * The keys the direct-permission tree offers: the live catalog when the viewer may read it, else the keys the
 * client was built with; plus every key the user holds directly or effectively. Sorted, unique.
 */
export function directPermissionKeys(
  liveCatalog: readonly string[] | null,
  builtInKeys: readonly string[],
  grants: UserGrants,
): readonly string[] {
  const keys = new Set(liveCatalog ?? builtInKeys);
  grants.permissions.forEach((grant) => keys.add(grant.key));
  if (liveCatalog === null) {
    grants.effective.forEach((entry) => keys.add(entry.key));
  }
  return [...keys].sort();
}

/** The effective permissions with each source's role names resolved from the response's own role lists. */
export function effectiveRows(grants: UserGrants): readonly EffectiveRow[] {
  const names = new Map<string, BilingualName>();
  grants.roles.forEach((role) => names.set(role.roleId, role));
  grants.virtualRoles.forEach((role) => names.set(role.roleId, role));
  return grants.effective.map((entry) => ({
    key: entry.key,
    scope: toPermissionScope(entry.scope),
    sources: entry.sources.map((source) => ({
      kind: source.kind,
      scope: toPermissionScope(source.scope),
      role: source.roleId === null ? null : names.get(source.roleId) ?? null,
    })),
  }));
}
