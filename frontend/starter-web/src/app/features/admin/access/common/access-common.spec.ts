import { ApiErrorCodes, ResponseSignatureError, ResponseSignatureFailures } from '@codelab/ng-security';
import { describe, expect, it } from 'vitest';
import { AppLanguages } from '../../../../core/i18n/language';
import { CATALOG, problem } from '../access.testing';
import { PermissionScope, PermissionSourceKind, VirtualRoleKind, toPermissionScope } from '../data/access.models';
import { apiErrorFrom, errorMessageKey, isLastManagerRefusal } from './access-errors';
import {
  labelText,
  localizedName,
  permissionLabelResolver,
  scopeLabelKey,
  sourceLabelKey,
  userKindLabelKey,
  virtualKindLabelKey,
} from './access-labels';
import { LeaveConfirmation } from './leave-confirmation';

describe('access errors', () => {
  it('parses an HTTP failure into its problem+json refusal', () => {
    const error = apiErrorFrom(problem(409, 'ACCESS_LAST_MANAGER'));
    expect(error.serverCode).toBe('ACCESS_LAST_MANAGER');
    expect(isLastManagerRefusal(error)).toBe(true);
    expect(isLastManagerRefusal(apiErrorFrom(problem(409, 'ACCESS_ROLE_NAME_TAKEN')))).toBe(false);
    expect(isLastManagerRefusal(null)).toBe(false);
  });

  it('turns an unverifiable answer into a refusal: FORBIDDEN for a 403, else INTERNAL_ERROR', () => {
    const forbidden = apiErrorFrom(new ResponseSignatureError(ResponseSignatureFailures.mismatch, { status: 403, url: '/api/x' }));
    expect(forbidden.code).toBe(ApiErrorCodes.forbidden);
    const failed = apiErrorFrom(new ResponseSignatureError(ResponseSignatureFailures.mismatch, { status: 500, url: '/api/x' }));
    expect(failed.code).toBe(ApiErrorCodes.internalError);
  });

  it('rethrows anything that is not an HTTP failure', () => {
    const bug = new TypeError('bug');
    expect(() => apiErrorFrom(bug)).toThrow(bug);
  });

  it('maps a code to its bundle key', () => {
    expect(errorMessageKey('ACCESS_ROLE_NOT_FOUND')).toBe('errors.ACCESS_ROLE_NOT_FOUND');
  });
});

describe('access labels', () => {
  const catalog = new Map(CATALOG.map((entry) => [entry.key, entry]));

  it('names a record in the current language, falling back to the other', () => {
    const named = { nameEn: 'Auditor', nameAr: 'مدقق' };
    expect(localizedName(named, AppLanguages.Arabic)).toBe('مدقق');
    expect(localizedName(named, AppLanguages.English)).toBe('Auditor');
    expect(localizedName({ nameEn: 'Auditor', nameAr: ' ' }, AppLanguages.Arabic)).toBe('Auditor');
  });

  it('labels a key by its translation, else by its catalog description', () => {
    const translated = permissionLabelResolver(catalog, (key) => key === 'permissions.dashboard.view');
    expect(translated('dashboard.view')).toEqual({ textKey: 'permissions.dashboard.view' });
    expect(translated('access.roles.view')).toEqual({ text: 'See roles.' });
  });

  it('labels a group by its translation, else by its last segment', () => {
    const resolve = permissionLabelResolver(catalog, (key) => key === 'admin.access.permission-groups.access');
    expect(resolve('access')).toEqual({ textKey: 'admin.access.permission-groups.access' });
    expect(resolve('access.roles')).toEqual({ text: 'roles' });
  });

  it('turns a label into text', () => {
    expect(labelText({ textKey: 'k' }, (key) => `[${key}]`)).toBe('[k]');
    expect(labelText({ text: 'plain' }, () => 'never')).toBe('plain');
  });

  it('maps the enums to their label keys', () => {
    expect(scopeLabelKey(PermissionScope.Global)).toBe('admin.access.scope.global');
    expect(scopeLabelKey(PermissionScope.OwnOrganization)).toBe('admin.access.scope.own-organization');
    expect(virtualKindLabelKey(VirtualRoleKind.AllExternalUsers)).toBe('admin.access.virtual-kind.all-external-users');
    expect(virtualKindLabelKey(null)).toBe('admin.access.virtual-kind.all-authenticated-users');
    expect(sourceLabelKey(PermissionSourceKind.BreakGlass)).toBe('admin.access.grants.source.break-glass');
    expect(sourceLabelKey(PermissionSourceKind.VirtualRole)).toBe('admin.access.grants.source.virtual-role');
    expect(userKindLabelKey(2)).toBe('admin.access.grants.kind-external');
    expect(userKindLabelKey(99)).toBe('admin.access.grants.kind-internal');
  });

  it('reads an unknown wire scope as the narrower one', () => {
    expect(toPermissionScope(1)).toBe(PermissionScope.Global);
    expect(toPermissionScope(2)).toBe(PermissionScope.OwnOrganization);
    expect(toPermissionScope(7)).toBe(PermissionScope.OwnOrganization);
  });
});

describe('LeaveConfirmation', () => {
  it('opens, then resolves with the answer and closes', async () => {
    const leave = new LeaveConfirmation();
    const answer = leave.ask();
    expect(leave.open()).toBe(true);
    leave.answer(true);
    await expect(answer).resolves.toBe(true);
    expect(leave.open()).toBe(false);
  });

  it('answers a superseded question with stay', async () => {
    const leave = new LeaveConfirmation();
    const first = leave.ask();
    const second = leave.ask();
    await expect(first).resolves.toBe(false);
    leave.answer(true);
    await expect(second).resolves.toBe(true);
  });
});
