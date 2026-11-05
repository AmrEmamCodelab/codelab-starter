// Public API of the template permission gate (`@codelab/ng-tools/permission-gates`).
export { EXCLUDED_SCRIPT_SUFFIXES, checkPermissionGates, findUngatedButtonsInSource, formatGateFindings, listGateSources } from './check.mjs';
export { ExitCodes, Subcommands, main, parsePermissionGateArguments, runPermissionGates } from './cli.mjs';
export { ACTION_OUTPUTS, GateFindingKinds, GateMarkers, SUBMIT_OUTPUTS, PERMISSION_GATE_FIX, findUngatedButtons, isActionButton } from './gate-rule.mjs';
export { extractInlineTemplates } from './inline-templates.mjs';
export { scanTemplate } from './template-scanner.mjs';
