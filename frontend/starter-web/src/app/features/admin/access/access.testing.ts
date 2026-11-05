import { HttpErrorResponse } from '@angular/common/http';
import type { Provider } from '@angular/core';
import { asId } from '@codelab/ng-security';
import { of, throwError, type Observable } from 'rxjs';
import { vi } from 'vitest';
import { PermissionSyncService } from '../../../core/permissions/permission-sync.service';
import { AccessAdminClient } from './data/access-api';
import {
  AccessIdDomains,
  PermissionScope,
  PermissionSourceKind,
  VirtualRoleKind,
  type CatalogPermission,
  type RoleDetail,
  type RoleId,
  type RoleSummary,
  type UserGrants,
} from './data/access.models';

/**
 * Spec-only fixtures and fakes for the access screens (nothing in the app imports this file). Ids have the wire
 * shape so the pages' `tryAsId` accepts them; the data is starter-neutral.
 */
const WIRE_PREFIX = 'm1_k1_';
const PAYLOAD_LENGTH = 48;

function wireToken(seed: string): string {
  return `${WIRE_PREFIX}${seed.padEnd(PAYLOAD_LENGTH, 'A')}`;
}

export const ROLE_REVIEWER_ID: RoleId = asId(AccessIdDomains.Role, wireToken('reviewer'));
export const ROLE_AUDITOR_ID: RoleId = asId(AccessIdDomains.Role, wireToken('auditor'));
export const ROLE_INTERNAL_ID: RoleId = asId(AccessIdDomains.Role, wireToken('internal'));
export const USER_ID = asId(AccessIdDomains.User, wireToken('user'));

export const CATALOG: readonly CatalogPermission[] = [
  { key: 'access.roles.manage', module: 'Access', description: 'Create and change roles.', isRetired: false },
  { key: 'access.roles.view', module: 'Access', description: 'See roles.', isRetired: false },
  { key: 'dashboard.view', module: 'Access', description: 'Open the dashboard.', isRetired: false },
  { key: 'reports.export', module: 'Reports', description: 'Export reports.', isRetired: true },
];

export const ROLE_SUMMARIES: readonly RoleSummary[] = [
  {
    id: ROLE_INTERNAL_ID, code: 'all-internal-users', nameEn: 'Internal users', nameAr: 'المستخدمون الداخليون',
    isActive: true, isVirtual: true, virtualKind: VirtualRoleKind.AllInternalUsers, permissionCount: 1,
  },
  {
    id: ROLE_REVIEWER_ID, code: 'case-reviewer', nameEn: 'Case reviewer', nameAr: 'مراجع الحالات',
    isActive: true, isVirtual: false, virtualKind: null, permissionCount: 2,
  },
  {
    id: ROLE_AUDITOR_ID, code: 'auditor', nameEn: 'Auditor', nameAr: 'مدقق',
    isActive: false, isVirtual: false, virtualKind: null, permissionCount: 0,
  },
];

export function roleDetail(overrides: Partial<RoleDetail> = {}): RoleDetail {
  return {
    id: ROLE_REVIEWER_ID,
    code: 'case-reviewer',
    nameEn: 'Case reviewer',
    nameAr: 'مراجع الحالات',
    isActive: true,
    isVirtual: false,
    virtualKind: null,
    permissions: ['dashboard.view', 'reports.export'],
    warnings: [],
    ...overrides,
  };
}

export function userGrants(overrides: Partial<UserGrants> = {}): UserGrants {
  return {
    user: { id: USER_ID, userName: 'sara', displayName: 'Sara Ali', isActive: true, kind: 1, isBreakGlass: false },
    roles: [{
      roleId: ROLE_REVIEWER_ID, code: 'case-reviewer', nameEn: 'Case reviewer', nameAr: 'مراجع الحالات',
      isActive: true, scope: PermissionScope.Global,
    }],
    permissions: [{ key: 'access.roles.view', scope: PermissionScope.OwnOrganization }],
    virtualRoles: [{
      roleId: ROLE_INTERNAL_ID, code: 'all-internal-users', nameEn: 'Internal users', nameAr: 'المستخدمون الداخليون',
      isActive: true, virtualKind: VirtualRoleKind.AllInternalUsers,
    }],
    effective: [
      {
        key: 'dashboard.view', scope: PermissionScope.Global, sources: [
          { kind: PermissionSourceKind.Role, roleId: ROLE_REVIEWER_ID, scope: PermissionScope.Global },
          { kind: PermissionSourceKind.VirtualRole, roleId: ROLE_INTERNAL_ID, scope: PermissionScope.OwnOrganization },
        ],
      },
      {
        key: 'access.roles.view', scope: PermissionScope.OwnOrganization, sources: [
          { kind: PermissionSourceKind.Direct, roleId: null, scope: PermissionScope.OwnOrganization },
        ],
      },
    ],
    warnings: [],
    ...overrides,
  };
}

/** A problem+json failure as the Angular HTTP client raises it. */
export function problem(status: number, code: string, errors?: Record<string, string[]>): HttpErrorResponse {
  return new HttpErrorResponse({
    status,
    error: { status, code, correlationId: 'trace-1', errors },
  });
}

export function fails<T>(error: unknown): Observable<T> {
  return throwError(() => error);
}

export type FakeAccessClient = { [K in keyof AccessAdminClient]: ReturnType<typeof vi.fn> };

/** A client whose reads answer the fixtures; override a method with `mockReturnValue(...)` per test. */
export function fakeAccessClient(): FakeAccessClient {
  return {
    permissions: vi.fn(() => of(CATALOG)),
    roles: vi.fn(() => of(ROLE_SUMMARIES)),
    role: vi.fn(() => of(roleDetail())),
    createRole: vi.fn(() => of(roleDetail())),
    updateRole: vi.fn((_id: RoleId, body: Partial<RoleDetail>) => of(roleDetail({ ...body, warnings: [] }))),
    searchUsers: vi.fn(() => of([])),
    userGrants: vi.fn(() => of(userGrants())),
    putUserGrants: vi.fn(() => of(userGrants())),
  };
}

export interface FakePermissionSync {
  readonly refresh: ReturnType<typeof vi.fn>;
}

export function fakePermissionSync(): FakePermissionSync {
  return { refresh: vi.fn(() => Promise.resolve()) };
}

export function accessTestProviders(client: FakeAccessClient, sync: FakePermissionSync = fakePermissionSync()): Provider[] {
  return [
    { provide: AccessAdminClient, useValue: client },
    { provide: PermissionSyncService, useValue: sync },
  ];
}
