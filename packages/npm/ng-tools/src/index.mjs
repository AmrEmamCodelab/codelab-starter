// Public API of @codelab/ng-tools. Sub-paths: ./eslint, ./stylelint, ./vectors, ./routes, ./permission-gates.
export {
  ALLOWED_DEPENDENCIES,
  ElementTypes,
  RESTRICTED_UI_LIBRARY_PATTERNS,
  UI_LIBRARY_MESSAGE,
  boundaryConfig,
  boundaryPolicies,
  codelabPlugin,
  createCodelabEslintConfig,
} from './eslint/index.mjs';
export { codelabStylelintConfig } from './stylelint/index.mjs';
export { DriftKinds, compareFolders, listFiles, syncFolders } from './vectors/sync.mjs';
export {
  PERMISSION_GUARD_MARKER,
  ROUTE_GATE_FIX,
  RouteFindingKinds,
  assertRoutesGated,
  findUngatedRoutes,
  formatRouteFindings,
  isMarkedPermissionGuard,
  markPermissionGuard,
} from './routes/route-gates.mjs';
export {
  GateMarkers,
  PERMISSION_GATE_FIX,
  checkPermissionGates,
  extractInlineTemplates,
  findUngatedButtons,
  findUngatedButtonsInSource,
  formatGateFindings,
} from './permission-gates/index.mjs';
