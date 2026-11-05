import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { PermissionKeys } from '../../../core/permissions/permission-keys.generated';
import { adminTestProviders, byTestId, grant } from '../organizations/admin.testing';
import { AdminPageComponent } from './admin-page.component';
import { ADMIN_TILES, visibleTiles } from './admin-tiles';

describe('admin tiles', () => {
  it('gates each tile by the key of the page it opens', () => {
    expect(ADMIN_TILES.map((tile) => [tile.id, tile.permission, tile.link.join('/')])).toEqual([
      ['users', PermissionKeys.UsersView, '//admin/users'],
      ['organizations', PermissionKeys.OrganizationsView, '//admin/organizations'],
      ['import', PermissionKeys.UsersImport, '//admin/import'],
      ['roles', PermissionKeys.AccessRolesView, '//admin/access/roles'],
    ]);
  });

  it('keeps only the tiles whose key is held', () => {
    const held = new Set<string>([PermissionKeys.UsersImport]);
    expect(visibleTiles((key) => held.has(key)).map((tile) => tile.id)).toEqual(['import']);
  });
});

describe('AdminPageComponent', () => {
  let fixture: ComponentFixture<AdminPageComponent>;
  let host: HTMLElement;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [AdminPageComponent], providers: adminTestProviders() });
  });

  function render(): void {
    fixture = TestBed.createComponent(AdminPageComponent);
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  }

  const tiles = () => [...host.querySelectorAll('[data-tile]')].map((tile) => tile.getAttribute('data-tile'));

  it('shows the tiles the user may open, linked to their pages', () => {
    grant(PermissionKeys.AccessAdminView, PermissionKeys.UsersView, PermissionKeys.AccessRolesView);
    render();
    expect(tiles()).toEqual(['users', 'roles']);
    expect(host.querySelector('[data-tile="users"] a')?.getAttribute('href')).toBe('/admin/users');
    expect(host.querySelector('[data-tile="roles"] a')?.getAttribute('href')).toBe('/admin/access/roles');
    expect(byTestId(fixture, 'admin-home-empty')).toBeNull();
  });

  it('follows a live grant and revoke', () => {
    grant(PermissionKeys.AccessAdminView);
    render();
    expect(tiles()).toEqual([]);
    expect(byTestId(fixture, 'admin-home-empty')).not.toBeNull();
    grant(PermissionKeys.AccessAdminView, PermissionKeys.OrganizationsView, PermissionKeys.UsersImport);
    fixture.detectChanges();
    expect(tiles()).toEqual(['organizations', 'import']);
    expect(byTestId(fixture, 'admin-home-empty')).toBeNull();
  });
});
