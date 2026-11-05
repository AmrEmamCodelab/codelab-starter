// Types for the template permission gate (`@codelab/ng-tools/permission-gates`).

export declare const GateMarkers: Readonly<{
  structuralDirective: '*hasPermission';
  templateBinding: '[hasPermission]';
  publicAction: 'data-public-action';
  clickBinding: '(click)';
  dgaButton: 'dga-button';
  nativeButton: 'button';
  templateElement: 'ng-template';
  formElement: 'form';
  typeAttribute: 'type';
  roleAttribute: 'role';
  buttonRole: 'button';
}>;

/** Outputs that make any element an action: `click` and `confirm`. */
export declare const ACTION_OUTPUTS: readonly string[];
/** Outputs of a `<form>` that submit it: `submit` and `ngSubmit`. */
export declare const SUBMIT_OUTPUTS: readonly string[];

export declare const GateFindingKinds: Readonly<{
  ungatedButton: 'ungated-button';
  literalKey: 'literal-key';
}>;

export type GateFindingKind = (typeof GateFindingKinds)[keyof typeof GateFindingKinds];

/** A finding in one template; `line` and `column` are 1-based, `element` is the tag name. */
export interface TemplateGateFinding {
  readonly kind: GateFindingKind;
  readonly line: number;
  readonly column: number;
  readonly element: string;
}

/** A finding of a folder check; `file` is relative to the folder, with forward slashes. */
export interface GateFinding extends TemplateGateFinding {
  readonly file: string;
}

export interface GateCheckResult {
  readonly findings: GateFinding[];
  readonly fileCount: number;
  readonly error?: string;
}

export interface TemplateEvent {
  readonly type: 'open' | 'close';
  readonly name: string;
  readonly attributes?: ReadonlyMap<string, string | null>;
  readonly selfClosing?: boolean;
  readonly line?: number;
  readonly column?: number;
}

export interface CliIo {
  log(line: string): void;
  error(line: string): void;
}

export declare const PERMISSION_GATE_FIX: string;
export declare const EXCLUDED_SCRIPT_SUFFIXES: readonly string[];
export declare const ExitCodes: Readonly<{ ok: number; failure: number; usage: number }>;
export declare const Subcommands: Readonly<{ permissionGates: 'permission-gates' }>;

export declare function scanTemplate(text: string): TemplateEvent[];
export declare function isActionButton(name: string, attributes: ReadonlyMap<string, string | null>, insideSubmittingForm?: boolean): boolean;
export declare function findUngatedButtons(text: string): TemplateGateFinding[];
export declare function extractInlineTemplates(source: string): { text: string; line: number }[];
export declare function findUngatedButtonsInSource(fileName: string, text: string): TemplateGateFinding[];
export declare function listGateSources(dir: string): string[];
export declare function checkPermissionGates(dir: string): GateCheckResult;
export declare function formatGateFindings(findings: ReadonlyArray<GateFinding>, fix?: string): string;
export declare function parsePermissionGateArguments(argv: readonly string[]): { options?: { dir: string; fix?: string }; error?: string };
export declare function runPermissionGates(options: { dir: string; fix?: string }, io?: CliIo): number;
export declare function main(argv: readonly string[], io?: CliIo): number;
