import { Directive, TemplateRef, ViewContainerRef, computed, effect, inject, input, untracked } from '@angular/core';
import { PermissionStore } from '../../core/permissions/permission.store';
import { PermissionMatchMode, holdsPermissions, type PermissionRequirement } from './permission-match';

/**
 * Renders its template only while the signed-in user holds the permission, and its `else` template otherwise. It
 * reads {@link PermissionStore} as a signal, so a grant or revoke picked up by the permission sync shows at once,
 * without a reload. The server still authorizes every call; this only hides what would be refused.
 *
 * @example
 * <dga-button *hasPermission="PermissionKeys.UsersManage" (click)="create()">...</dga-button>
 * <section *hasPermission="[PermissionKeys.AccessRolesView, PermissionKeys.AccessGrantsView]; mode: PermissionMatchMode.All; else noAccess">
 * <ng-template #noAccess>...</ng-template>
 */
@Directive({ selector: '[hasPermission]' })
export class HasPermissionDirective {
  /** The key, or keys matched by `mode` (default: any). An empty list never renders. */
  readonly hasPermission = input.required<PermissionRequirement>();
  readonly hasPermissionMode = input<PermissionMatchMode>(PermissionMatchMode.Any);
  /** Rendered when the permission is not held. */
  readonly hasPermissionElse = input<TemplateRef<unknown> | null>(null);

  private readonly store = inject(PermissionStore);
  private readonly template = inject<TemplateRef<unknown>>(TemplateRef);
  private readonly container = inject(ViewContainerRef);
  private shown: TemplateRef<unknown> | null = null;

  readonly allowed = computed(() =>
    holdsPermissions((key) => this.store.has(key), this.hasPermission(), this.hasPermissionMode()));

  constructor() {
    effect(() => {
      const next = this.allowed() ? this.template : this.hasPermissionElse();
      untracked(() => this.render(next));
    });
  }

  private render(next: TemplateRef<unknown> | null): void {
    if (next === this.shown) {
      return;
    }
    this.container.clear();
    this.shown = next;
    if (next) {
      this.container.createEmbeddedView(next);
    }
  }
}
