import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ApiErrorCodes, HttpStatus } from '@codelab/ng-security';
import { Subject, of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeClient } from '../api/clients/me.client';
import type { PermissionsResponse } from '../api/clients/api.models';
import { PermissionSyncService } from './permission-sync.service';
import { PermissionStore } from './permission.store';

describe('PermissionSyncService', () => {
  let permissions: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    permissions = vi.fn().mockReturnValue(of({ permissions: ['dashboard.view'] }));
    TestBed.configureTestingModule({ providers: [{ provide: MeClient, useValue: { permissions } }] });
  });

  it('loads the keys into the store', async () => {
    await TestBed.inject(PermissionSyncService).refresh();
    expect(TestBed.inject(PermissionStore).has('dashboard.view')).toBe(true);
  });

  it('shares one request between concurrent refreshes', async () => {
    const pending = new Subject<PermissionsResponse>();
    permissions.mockReturnValue(pending);
    const sync = TestBed.inject(PermissionSyncService);
    const both = Promise.all([sync.refresh(), sync.refresh()]);
    pending.next({ permissions: ['a.b'] });
    pending.complete();
    await both;
    expect(permissions).toHaveBeenCalledTimes(1);
  });

  it('propagates a failed refresh and keeps the previous keys', async () => {
    const sync = TestBed.inject(PermissionSyncService);
    await sync.refresh();
    permissions.mockReturnValue(throwError(() => new Error('offline')));
    await expect(sync.refresh()).rejects.toThrow('offline');
    expect(TestBed.inject(PermissionStore).has('dashboard.view')).toBe(true);
  });

  it('ensureLoaded fetches once, then answers from the store', async () => {
    const sync = TestBed.inject(PermissionSyncService);
    await expect(sync.ensureLoaded()).resolves.toBe(true);
    await expect(sync.ensureLoaded()).resolves.toBe(true);
    expect(permissions).toHaveBeenCalledTimes(1);
  });

  it('ensureLoaded resolves false when the sync fails', async () => {
    permissions.mockReturnValue(throwError(() => new Error('offline')));
    await expect(TestBed.inject(PermissionSyncService).ensureLoaded()).resolves.toBe(false);
  });

  it('records why the sync failed (code and reference code), and forgets it after a good sync', async () => {
    const serverFault = new HttpErrorResponse({
      status: HttpStatus.serverError,
      error: { code: ApiErrorCodes.internalError, correlationId: 'abcde12345', referenceCode: 'ABCDE-12345' },
    });
    permissions.mockReturnValueOnce(throwError(() => serverFault));
    const sync = TestBed.inject(PermissionSyncService);
    await expect(sync.ensureLoaded()).resolves.toBe(false);
    expect(sync.lastFailure()).toMatchObject({ code: ApiErrorCodes.internalError, referenceCode: 'ABCDE-12345' });
    await expect(sync.ensureLoaded()).resolves.toBe(true);
    expect(sync.lastFailure()).toBeNull();
  });

  it('reads a non-HTTP sync failure as a server fault', async () => {
    permissions.mockReturnValue(throwError(() => new Error('offline')));
    const sync = TestBed.inject(PermissionSyncService);
    await sync.ensureLoaded();
    expect(sync.lastFailure()?.status).toBe(HttpStatus.serverError);
  });

  it('clear empties the store', async () => {
    const sync = TestBed.inject(PermissionSyncService);
    await sync.refresh();
    sync.clear();
    expect(TestBed.inject(PermissionStore).loaded()).toBe(false);
  });
});
