import { HttpErrorResponse } from '@angular/common/http';
import { ApiErrorCodes, HttpStatus, ResponseSignatureError, ResponseSignatureFailures } from '@codelab/ng-security';
import { Subject, of, throwError } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { AppLanguages } from '../../../../core/i18n/language';
import { organization, problem } from '../admin.testing';
import { apiErrorFrom, errorCodeKey } from './admin-api-error';
import {
  OrganizationKind,
  UNKNOWN_KIND_LABEL_KEY,
  isOrganizationKind,
  organizationKindLabelKey,
  organizationOption,
} from './organization.models';
import { ActiveFilter, PagedListState, isActiveFor, selectValue, selectedNumber, type ApiPage } from './paged-list';

describe('admin API errors', () => {
  it('parses an HTTP failure as problem+json', () => {
    const error = apiErrorFrom(problem(HttpStatus.conflict, 'USERS_ORGANIZATION_CODE_TAKEN'));
    expect(error.serverCode).toBe('USERS_ORGANIZATION_CODE_TAKEN');
    expect(error.referenceCode).toBeTruthy();
  });

  it('turns an answer whose signature could not be verified into a refusal with no server detail', () => {
    const unverified = (status: number) =>
      new ResponseSignatureError(ResponseSignatureFailures.mismatch, { status, url: 'http://api.test/api/admin/users' });
    expect(apiErrorFrom(unverified(HttpStatus.forbidden))).toEqual({ status: HttpStatus.forbidden, code: ApiErrorCodes.forbidden, fieldErrors: [] });
    expect(apiErrorFrom(unverified(HttpStatus.serverError)).code).toBe(ApiErrorCodes.internalError);
  });

  it('rethrows anything that is not an HTTP failure', () => {
    const bug = new TypeError('bug');
    expect(() => apiErrorFrom(bug)).toThrow(bug);
  });

  it('keys an error code under errors.*', () => {
    expect(errorCodeKey('USERS_IMPORT_ROW_EMAIL_INVALID')).toBe('errors.USERS_IMPORT_ROW_EMAIL_INVALID');
  });
});

describe('organization models', () => {
  it('labels known kinds and falls back for an unknown one', () => {
    expect(organizationKindLabelKey(OrganizationKind.ExternalOrganization)).toBe('admin.organizations.kinds.external-organization');
    expect(organizationKindLabelKey(9)).toBe(UNKNOWN_KIND_LABEL_KEY);
    expect(organizationKindLabelKey(null)).toBe(UNKNOWN_KIND_LABEL_KEY);
    expect(isOrganizationKind('1')).toBe(false);
  });

  it('names a picker option in the current language (Arabic by default)', () => {
    const row = organization({ nameAr: 'arabic-name' });
    expect(organizationOption(row, AppLanguages.English).name).toBe('Head office');
    expect(organizationOption(row, AppLanguages.Arabic)).toEqual({ id: row.id, name: 'arabic-name', kind: row.kind });
  });
});

describe('list filters', () => {
  it('maps the active filter to the isActive query value', () => {
    expect(isActiveFor(ActiveFilter.All)).toBeNull();
    expect(isActiveFor(ActiveFilter.Active)).toBe(true);
    expect(isActiveFor(ActiveFilter.Inactive)).toBe(false);
  });

  it('parses a select value only into an allowed number', () => {
    const allowed = [1, 2] as const;
    expect(selectedNumber('2', allowed)).toBe(2);
    expect(selectedNumber('', allowed)).toBeNull();
    expect(selectedNumber('7', allowed)).toBeNull();
    expect(selectedNumber('x', allowed)).toBeNull();
  });

  it('reads the value of the select that fired the event', () => {
    const select = document.createElement('select');
    select.append(new Option('a', 'a-value'));
    const event = new Event('change');
    select.dispatchEvent(event);
    expect(selectValue(event)).toBe('a-value');
    expect(selectValue(new Event('change'))).toBe('');
  });
});

describe('PagedListState', () => {
  it('loads the rows and the total (sent as a string or a number)', async () => {
    const state = new PagedListState<number, string>(() => of({ items: ['a', 'b'], total: '12' }));
    await state.load(1);
    expect(state.items()).toEqual(['a', 'b']);
    expect(state.total()).toBe(12);
    expect(state.loading()).toBe(false);
    expect(state.error()).toBeNull();
  });

  it('keeps the failure of a load as an ApiError', async () => {
    const state = new PagedListState<number, string>(() =>
      throwError(() => new HttpErrorResponse({ status: HttpStatus.serverError })));
    await state.load(1);
    expect(state.error()?.status).toBe(HttpStatus.serverError);
    expect(state.loading()).toBe(false);
  });

  it('clears the rows of an earlier load when a later load fails, even when its answer is unverifiable', async () => {
    let fail = false;
    const state = new PagedListState<number, string>(() =>
      fail
        ? throwError(() => new ResponseSignatureError(ResponseSignatureFailures.mismatch, { status: HttpStatus.forbidden, url: null }))
        : of({ items: ['a', 'b'], total: 2 }));
    await state.load(1);
    fail = true;
    await state.load(2);
    expect(state.items()).toEqual([]);
    expect(state.total()).toBe(0);
    expect(state.error()?.code).toBe(ApiErrorCodes.forbidden);
  });

  it('keeps only the answer of the latest request', async () => {
    const slow = new Subject<ApiPage<string>>();
    const state = new PagedListState<number, string>((page) => (page === 1 ? slow : of({ items: ['new'], total: 1 })));
    const first = state.load(1);
    await state.load(2);
    slow.next({ items: ['old'], total: 1 });
    slow.complete();
    await first;
    expect(state.items()).toEqual(['new']);
    expect(state.loading()).toBe(false);
  });
});
