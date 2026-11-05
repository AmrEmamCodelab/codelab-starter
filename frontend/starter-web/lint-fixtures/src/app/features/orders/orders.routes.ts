// Lint fixture: route tables for the route-table permission gate (tools/lint-fixtures.spec.ts). A route table is
// data checked at runtime, not a content-rule violation, so both tables are plain .ts files.
import type { CanActivateFn, Routes } from '@angular/router';
import { OrdersPage } from './orders-page';

export const FixturePermissionKeys = {
  OrdersView: 'orders.view',
  OrdersManage: 'orders.manage',
} as const;

export const FIXTURE_PERMISSION_DATA_KEY = 'permission';
export const FIXTURE_PUBLIC_PATHS: readonly string[] = ['login', '**', ''];

/** The tag the route gate looks for on a permission guard (the app's permissionGuard() sets the same one). */
const PERMISSION_GUARD_MARKER = Symbol.for('codelab.ng-tools.permission-guard');

/** A permission guard stand-in, tagged like the app's `permissionGuard()`. */
export const fixturePermissionGuard: CanActivateFn = Object.defineProperty((() => true) as CanActivateFn, PERMISSION_GUARD_MARKER, { value: true });

/** A guard that is not a permission guard (like authGuard): it does not count. */
export const fixtureAuthGuard: CanActivateFn = () => true;

/** Every page gated by a key on itself or an ancestor, or public: must pass. */
export const GATED_ORDER_ROUTES: Routes = [
  { path: 'login', component: OrdersPage },
  {
    path: '',
    component: OrdersPage,
    children: [
      { path: 'orders', canActivate: [fixturePermissionGuard], data: { [FIXTURE_PERMISSION_DATA_KEY]: FixturePermissionKeys.OrdersView }, component: OrdersPage },
      {
        path: 'orders/manage',
        canActivate: [fixturePermissionGuard],
        data: { [FIXTURE_PERMISSION_DATA_KEY]: FixturePermissionKeys.OrdersManage },
        children: [{ path: 'edit', component: OrdersPage }],
      },
      { path: 'old-orders', redirectTo: 'orders' },
    ],
  },
  { path: '**', component: OrdersPage },
];

/** The shell gains a page with no key: must fail with /orders/export. */
export const UNGATED_ORDER_ROUTES: Routes = [
  {
    path: '',
    component: OrdersPage,
    children: [
      { path: 'orders', canActivate: [fixturePermissionGuard], data: { [FIXTURE_PERMISSION_DATA_KEY]: FixturePermissionKeys.OrdersView }, component: OrdersPage },
      { path: 'orders/export', component: OrdersPage },
    ],
  },
];

/** A page with a key but only a non-permission guard: must fail with missing-guard at /orders/report. */
export const UNGUARDED_ORDER_ROUTES: Routes = [
  {
    path: '',
    component: OrdersPage,
    children: [
      { path: 'orders/report', canActivate: [fixtureAuthGuard], data: { [FIXTURE_PERMISSION_DATA_KEY]: FixturePermissionKeys.OrdersView }, component: OrdersPage },
    ],
  },
];
