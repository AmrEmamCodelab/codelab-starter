import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  RouterOutlet,
  provideRouter,
  type ActivatedRouteSnapshot,
  type PartialMatchRouteSnapshot,
  type Route,
  type RouterStateSnapshot,
  type Routes,
  type UrlSegment,
} from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../../../core/auth/auth.service';
import { PermissionKeys, type PermissionKey } from '../../../core/permissions/permission-keys.generated';
import { PermissionStore } from '../../../core/permissions/permission.store';
import { PermissionSyncService } from '../../../core/permissions/permission-sync.service';
import { SERVICE_REQUESTS_PERMISSION_KEYS } from '../data';
import { SERVICE_REQUESTS_REQUESTER_DEFAULT_BASE_LINK, SERVICE_REQUESTS_REQUESTER_PATH } from './requester-paths';
import { requesterLanding, requesterShellMatch } from './requester-landing';

const VIEW_OWN = SERVICE_REQUESTS_PERMISSION_KEYS.ViewOwn;
const STATE = { url: '/' } as RouterStateSnapshot;

function segments(...paths: string[]): UrlSegment[] {
  return paths.map((path) => ({ path, parameters: {} }) as UrlSegment);
}

describe('requesterShellMatch', () => {
  const match = (...paths: string[]) => requesterShellMatch({} as Route, segments(...paths), {} as PartialMatchRouteSnapshot);

  it('does not claim the app root, so / reaches the staff dashboard', () => {
    expect(match()).toBe(false);
  });

  it('does not claim a staff page', () => {
    expect(match('admin')).toBe(false);
  });

  it('claims the requester area and its pages', () => {
    expect(match(SERVICE_REQUESTS_REQUESTER_PATH)).toBe(true);
    expect(match(SERVICE_REQUESTS_REQUESTER_PATH, 'mine')).toBe(true);
  });
});

describe('requesterLanding', () => {
  const authenticated = signal(true);
  let ensureLoaded: ReturnType<typeof vi.fn>;
  let store: PermissionStore;

  beforeEach(() => {
    authenticated.set(true);
    ensureLoaded = vi.fn().mockResolvedValue(true);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { isAuthenticated: authenticated } },
        { provide: PermissionSyncService, useValue: { ensureLoaded, lastFailure: () => null } },
      ],
    });
    store = TestBed.inject(PermissionStore);
  });

  const run = (gate: readonly PermissionKey[] | PermissionKey = VIEW_OWN) =>
    TestBed.runInInjectionContext(() => requesterLanding(gate)({} as ActivatedRouteSnapshot, STATE));

  it('lets staff and admins (dashboard.view) through to the staff dashboard', async () => {
    store.set([PermissionKeys.DashboardView, VIEW_OWN]);
    await expect(run()).resolves.toBe(true);
  });

  it('sends a requester without dashboard.view to the requester dashboard', async () => {
    store.set([VIEW_OWN]);
    expect(String(await run())).toBe(SERVICE_REQUESTS_REQUESTER_DEFAULT_BASE_LINK);
  });

  it('leaves a user with neither key to the dashboard permission guard', async () => {
    store.set([]);
    await expect(run()).resolves.toBe(true);
  });

  it('never redirects on a gate that is not generated yet (empty list)', async () => {
    store.set([VIEW_OWN]);
    await expect(run([])).resolves.toBe(true);
  });

  it('leaves a failed sync to the dashboard permission guard', async () => {
    ensureLoaded.mockResolvedValue(false);
    await expect(run()).resolves.toBe(true);
  });

  it('leaves a signed-out user to the dashboard permission guard without syncing', async () => {
    authenticated.set(false);
    await expect(run()).resolves.toBe(true);
    expect(ensureLoaded).not.toHaveBeenCalled();
  });
});

@Component({ template: '<router-outlet />', imports: [RouterOutlet] })
class FrameStub {}

@Component({ template: 'staff-dashboard' })
class StaffDashboardStub {}

@Component({ template: 'requester-dashboard' })
class RequesterDashboardStub {}

describe('the requester shell at the app root (real router)', () => {
  // The app's shape: an empty-path requester frame before the empty-path staff frame.
  const table: Routes = [
    {
      path: '',
      canMatch: [requesterShellMatch],
      component: FrameStub,
      children: [{ path: SERVICE_REQUESTS_REQUESTER_PATH, children: [{ path: '', pathMatch: 'full', component: RequesterDashboardStub }] }],
    },
    { path: '', component: FrameStub, children: [{ path: '', pathMatch: 'full', component: StaffDashboardStub }] },
  ];

  it('sends / to the staff dashboard, not an empty requester frame', async () => {
    TestBed.configureTestingModule({ providers: [provideRouter(table)] });
    const harness = await RouterTestingHarness.create('/');
    expect(harness.routeNativeElement?.textContent).toContain('staff-dashboard');
  });

  it('still opens the requester dashboard under its own path', async () => {
    TestBed.configureTestingModule({ providers: [provideRouter(table)] });
    const harness = await RouterTestingHarness.create(SERVICE_REQUESTS_REQUESTER_DEFAULT_BASE_LINK);
    expect(harness.routeNativeElement?.textContent).toContain('requester-dashboard');
  });
});
