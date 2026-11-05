// Public API of @codelab/ng-tools. Sub-paths: ./eslint, ./stylelint, ./vectors, ./routes, ./permission-gates. The RTL
// lint, icon subsetting and the i18n check (0.5) are exported from the root only.
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
export {
  DEFAULT_DIRECTION_FILES,
  FIXED_DIRECTION_MARKER,
  RTL_LINT_FIX,
  RtlFindingKinds,
  checkRtl,
  findRtlFindingsInSource,
  formatRtlFindings,
  runRtlLint,
} from './rtl/index.mjs';
export {
  DEFAULT_SUBSET_STEM,
  collectIconUsage,
  driftedSubsetOutputs,
  findUnknownIconNames,
  findUsedIconNames,
  readIconMap,
  renderIconMap,
  runIconsSubset,
  subsetIconMap,
  subsetOutputs,
} from './icons/index.mjs';
export {
  I18N_CHECK_FIX,
  I18nProblemKinds,
  REQUIRED_LANGUAGES,
  checkI18n,
  findLeavesWithoutArabic,
  findUsedKeys,
  formatI18nProblems,
  readTranslations,
  runI18nCheck,
} from './i18n/index.mjs';
export { CLI_USAGE, Subcommands, main as runCodelabNgTools } from './cli/main.mjs';
export { ExitCodes } from './shared/arguments.mjs';
