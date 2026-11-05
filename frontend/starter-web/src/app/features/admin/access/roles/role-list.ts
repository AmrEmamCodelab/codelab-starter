import { FIRST_PAGE } from '../../../../patterns';
import type { CreateRoleBody, RoleDetail, RoleSummary, UpdateRoleBody } from '../data/access.models';

/** Rows per page of the roles list (filtered and paged in the browser: the server returns every role). */
export const ROLE_LIST_PAGE_SIZE = 20;

/** The create-role form as typed. */
export interface NewRoleDraft {
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

export const EMPTY_ROLE_DRAFT: NewRoleDraft = Object.freeze({ code: '', nameEn: '', nameAr: '' });

function normalized(text: string): string {
  return text.trim().toLocaleLowerCase();
}

/** True when the role's code or either name contains `search` (case-insensitive; a blank search matches all). */
export function matchesRoleSearch(role: RoleSummary, search: string): boolean {
  const needle = normalized(search);
  if (needle === '') {
    return true;
  }
  return [role.code, role.nameEn, role.nameAr].some((text) => normalized(text).includes(needle));
}

export function filterRoles(roles: readonly RoleSummary[], search: string): readonly RoleSummary[] {
  return roles.filter((role) => matchesRoleSearch(role, search));
}

/** The rows of the 1-based `page`; a page past the end shows the last page. */
export function pageOf<T>(rows: readonly T[], page: number, pageSize: number): readonly T[] {
  const lastPage = Math.max(FIRST_PAGE, Math.ceil(rows.length / pageSize));
  const current = Math.min(Math.max(FIRST_PAGE, Math.floor(page)), lastPage);
  const start = (current - FIRST_PAGE) * pageSize;
  return rows.slice(start, start + pageSize);
}

/** The POST body of a new role: trimmed fields and no permissions yet (they are chosen in the role editor). */
export function createRoleBody(draft: NewRoleDraft): CreateRoleBody {
  return { code: draft.code.trim(), nameEn: draft.nameEn.trim(), nameAr: draft.nameAr.trim(), permissions: [] };
}

/** The PUT body that only flips a role's active state, keeping its names and permissions as they are. */
export function activationBody(role: RoleDetail, isActive: boolean, confirmBreakGlassOnly: boolean): UpdateRoleBody {
  return {
    nameEn: role.nameEn,
    nameAr: role.nameAr,
    permissions: [...role.permissions],
    isActive,
    confirmBreakGlassOnly,
  };
}

/** The permission count as a number (the generated type allows the int32 string form). */
export function permissionCountOf(role: RoleSummary): number {
  return Number(role.permissionCount);
}
