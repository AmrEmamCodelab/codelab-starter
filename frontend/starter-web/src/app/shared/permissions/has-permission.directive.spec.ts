import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { PermissionKeys, type PermissionKey } from '../../core/permissions/permission-keys.generated';
import { PermissionStore } from '../../core/permissions/permission.store';
import { HasPermissionDirective } from './has-permission.directive';
import { PermissionMatchMode, type PermissionRequirement } from './permission-match';

/** Living documentation: gating an action button and a section. */
@Component({
  imports: [HasPermissionDirective],
  template: `
    <button *hasPermission="PermissionKeys.AccessAdminView" type="button" class="admin">admin</button>
    <section *hasPermission="required(); mode: mode(); else denied" class="both">both</section>
    <ng-template #denied><p class="denied">denied</p></ng-template>
  `,
})
class GatedHost {
  protected readonly PermissionKeys = PermissionKeys;
  readonly required = signal<PermissionRequirement>([PermissionKeys.AccessAdminView, PermissionKeys.DashboardView]);
  readonly mode = signal(PermissionMatchMode.All);
}

describe('HasPermissionDirective', () => {
  let fixture: ComponentFixture<GatedHost>;
  let host: HTMLElement;
  let store: PermissionStore;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [GatedHost] });
    store = TestBed.inject(PermissionStore);
    fixture = TestBed.createComponent(GatedHost);
    host = fixture.nativeElement as HTMLElement;
  });

  function grant(...keys: PermissionKey[]): void {
    store.set(keys);
    fixture.detectChanges();
  }

  it('renders nothing (but the else template) before any permission is held', () => {
    fixture.detectChanges();
    expect(host.querySelector('.admin')).toBeNull();
    expect(host.querySelector('.both')).toBeNull();
    expect(host.querySelector('.denied')).not.toBeNull();
  });

  it('shows the content when the key is held', () => {
    grant(PermissionKeys.AccessAdminView);
    expect(host.querySelector('.admin')).not.toBeNull();
    expect(host.querySelector('.both')).toBeNull();
    expect(host.querySelector('.denied')).not.toBeNull();
  });

  it('follows a live grant and revoke without re-creating the host', () => {
    grant(PermissionKeys.AccessAdminView, PermissionKeys.DashboardView);
    expect(host.querySelector('.both')).not.toBeNull();
    expect(host.querySelector('.denied')).toBeNull();
    const shown = host.querySelector('.admin');

    grant(PermissionKeys.AccessAdminView);
    expect(host.querySelector('.both')).toBeNull();
    expect(host.querySelector('.denied')).not.toBeNull();
    expect(host.querySelector('.admin')).toBe(shown);

    store.clear();
    fixture.detectChanges();
    expect(host.querySelector('.admin')).toBeNull();
  });

  it('matches any key in Any mode and reacts to a changed requirement', () => {
    grant(PermissionKeys.DashboardView);
    expect(host.querySelector('.both')).toBeNull();
    fixture.componentInstance.mode.set(PermissionMatchMode.Any);
    fixture.detectChanges();
    expect(host.querySelector('.both')).not.toBeNull();
    fixture.componentInstance.required.set([]);
    fixture.detectChanges();
    expect(host.querySelector('.both')).toBeNull();
  });
});
