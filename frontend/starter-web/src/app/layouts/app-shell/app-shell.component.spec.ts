import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { PermissionKeys } from '../../core/permissions/permission-keys.generated';
import { PermissionStore } from '../../core/permissions/permission.store';
import { RoutePaths } from '../../core/routing/route-paths';
import { fakeAuth, layoutTestProviders } from '../layout.testing';
import { AppShellComponent } from './app-shell.component';
import { SHELL_NAV_ITEMS, type ShellNavItem } from './shell-nav';

const NAV_ITEMS: readonly ShellNavItem[] = [
  {
    path: RoutePaths.Dashboard,
    labelKey: 'layout.nav.dashboard',
    icon: 'dashboard',
    targets: [{ permission: PermissionKeys.DashboardView, link: ['/'] }],
  },
  {
    path: RoutePaths.Admin,
    labelKey: 'layout.nav.admin',
    icon: 'lock',
    targets: [
      { permission: PermissionKeys.AccessAdminView, link: ['/', 'admin'] },
      { permission: PermissionKeys.UsersView, link: ['/', 'admin', 'users'] },
    ],
  },
];

describe('AppShellComponent', () => {
  let store: PermissionStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AppShellComponent],
      providers: [provideRouter([]), ...layoutTestProviders(fakeAuth()), { provide: SHELL_NAV_ITEMS, useValue: NAV_ITEMS }],
    });
    store = TestBed.inject(PermissionStore);
  });

  function render(): HTMLElement {
    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function navLabels(host: HTMLElement): string[] {
    return [...host.querySelectorAll('.shell__nav a > span')].map(label => label.textContent?.trim() ?? '');
  }

  it('shows only the navigation the user holds keys for', () => {
    store.set([PermissionKeys.DashboardView]);
    expect(navLabels(render())).toEqual(['layout.nav.dashboard']);
  });

  it('shows every entry to a user holding all keys', () => {
    store.set([PermissionKeys.DashboardView, PermissionKeys.AccessAdminView]);
    expect(navLabels(render())).toEqual(['layout.nav.dashboard', 'layout.nav.admin']);
  });

  it('offers the admin entry to a users.view-only user, linking to the users page', () => {
    store.set([PermissionKeys.UsersView]);
    const host = render();
    expect(navLabels(host)).toEqual(['layout.nav.admin']);
    expect(host.querySelector('.shell__nav a')?.getAttribute('href')).toBe('/admin/users');
  });

  it('follows a permission change without a reload', async () => {
    store.set([PermissionKeys.DashboardView]);
    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();
    store.set([PermissionKeys.DashboardView, PermissionKeys.AccessAdminView]);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(navLabels(fixture.nativeElement as HTMLElement)).toHaveLength(2);
  });

  it('renders the header actions, a labelled navigation and the page outlet', () => {
    const host = render();
    expect(host.querySelector('nav')?.getAttribute('aria-label')).toBe('layout.shell.nav-label');
    expect(host.querySelector('app-language-switcher')).not.toBeNull();
    expect(host.querySelector('app-user-profile-menu')).not.toBeNull();
    expect(host.querySelector('main router-outlet')).not.toBeNull();
  });
});
