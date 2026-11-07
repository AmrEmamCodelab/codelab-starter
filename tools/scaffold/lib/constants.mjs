// Every name, path, pattern and message of the scaffold generators (new-module, new-endpoint, new-permission,
// new-lookup; new-transition keeps its own in lib/transition/constants.mjs).
// No other file in tools/scaffold holds a literal of these.
//
// RENAME SAFETY (ADR-003): the starter-named literals below (SOLUTION_TOKEN, SOLUTION_FILE, API_PROJECT, ...) are
// written whole, in the form a fork's renameMap finds, so init's rename rewrites them with the files they name.

/** Process exit codes shared by every scaffold CLI (the same contract as the fork tools). */
export const ExitCode = Object.freeze({
  /** Success, including an idempotent no-op. */
  ok: 0,
  /** Refused: nothing was changed. */
  refused: 1,
  /** Usage or internal error. */
  usage: 2,
});

/** The solution token every starter project, namespace and assembly name derives from. */
export const SOLUTION_TOKEN = 'Starter';
export const SOLUTION_FILE = 'codelab-starter.slnx';
export const API_PROJECT = 'backend/src/Starter.Api/Starter.Api.csproj';
export const MANIFEST_FILE = 'codelab.starter.json';
/** The solution folder every backend project is listed under. */
export const BACKEND_SOLUTION_FOLDER = '/backend/';
/** The label of the host project's ItemGroup that lists modules. */
export const MODULES_ITEM_GROUP_LABEL = 'Modules';

/** Kernel project names (Starter.<Name>) and other names a module may not take. */
export const KERNEL_PROJECTS = Object.freeze(['Domain', 'Application', 'Infrastructure', 'Api']);
export const RESERVED_NAMES = Object.freeze(['Kernel', 'Modules', 'Shared', 'Core', 'Common', 'Tests', 'Codelab', SOLUTION_TOKEN]);

/** Where a module lives (ADR-004). */
export const MODULES_ROOT = 'backend/src/Modules';
export const MODULE_TESTS_ROOT = 'backend/tests/Modules';
export const MODULE_TEMPLATE_SET = 'module';
export const TEMPLATE_EXTENSION = '.tmpl';
/** Template file names: path tokens are written as __Token__ because braces are awkward in file names. */
export const PATH_TOKEN = /__([A-Za-z]+)__/g;
/** Content tokens: {{Token}}. */
export const CONTENT_TOKEN = /\{\{([A-Za-z]+)\}\}/g;

/** Name shapes. */
export const PASCAL_NAME_PATTERN = /^[A-Z][a-z0-9]+(?:[A-Z][a-z0-9]+)*$/;
export const KEBAB_NAME_PATTERN = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/;
export const ROUTE_SEGMENT_PATTERN = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*(?:\/[a-z][a-z0-9]*(?:-[a-z0-9]+)*)*$/;
/**
 * The --route value of an endpoint on the module root itself (api/<module>). '/' and '' are accepted too, but Git Bash
 * rewrites a lone '/' into a Windows path and Windows PowerShell 5.1 drops an empty argument, so docs show '.'.
 */
export const MODULE_ROOT_ROUTE = '.';
export const MODULE_ROOT_ROUTE_ALIASES = Object.freeze(['', '/']);
/** ApiRoutes.Prefix (Starter.Infrastructure.Hosting): the first segment of every API route. */
export const API_ROUTE_PREFIX = 'api';
/** Separates the segments of an endpoint route. */
export const ROUTE_SEPARATOR = '/';
/** PermissionKeyFormat.Pattern (Starter.Application), mirrored so a key is checked before any file is written. */
export const PERMISSION_KEY_PATTERN = /^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$/;
export const PERMISSION_KEY_MAX_LENGTH = 128;
/** PascalCase names stay short: the kebab form is the PublicId domain label (at most 32 characters). */
export const MAX_NAME_LENGTH = 24;

/** HTTP methods an endpoint can take, and how each one is generated. */
export const HttpMethod = Object.freeze({
  GET: 'GET',
  POST: 'POST',
  PUT: 'PUT',
  DELETE: 'DELETE',
});
export const ENDPOINT_SHAPES = Object.freeze({
  [HttpMethod.GET]: { templateSet: 'endpoint-get', hasId: true, hasBody: false, defaultAction: 'view', errorSuffix: 'NotFound', errorCodeSuffix: 'NOT_FOUND', errorSummary: 'The record to read does not exist' },
  [HttpMethod.POST]: { templateSet: 'endpoint-post', hasId: false, hasBody: true, defaultAction: 'manage', errorSuffix: 'Refused', errorCodeSuffix: 'REFUSED', errorSummary: 'The store refused the new record' },
  [HttpMethod.PUT]: { templateSet: 'endpoint-put', hasId: true, hasBody: true, defaultAction: 'manage', errorSuffix: 'NotFound', errorCodeSuffix: 'NOT_FOUND', errorSummary: 'The record to update does not exist' },
  [HttpMethod.DELETE]: { templateSet: 'endpoint-delete', hasId: true, hasBody: false, defaultAction: 'manage', errorSuffix: 'NotFound', errorCodeSuffix: 'NOT_FOUND', errorSummary: 'The record to delete does not exist' },
});
/**
 * The id segment of an endpoint that addresses one record, as it reads in docs and as --route may place it
 * ('{id}/draft', 'widgets/{id}/archive'); without it the id goes last.
 */
export const ID_ROUTE_DOC = '{id}';
/** The namespaces an endpoint's registrations need in <Module>ModuleRegistration.cs. */
export const VALIDATION_NAMESPACE = 'Starter.Application.Validation';

/** The permission every new module starts with. */
export const DEFAULT_MODULE_ACTION = 'view';

/** Markers the generators insert before. Generated files carry them; hand-written files use the fallbacks below. */
export const Markers = Object.freeze({
  permissionKeys: '// scaffold:permission-keys',
  permissionCatalog: '// scaffold:permission-catalog',
  registrations: '// scaffold:registrations',
  errorCodes: '// scaffold:error-codes',
  lookupTypes: '// scaffold:lookup-types',
});
/** Fallback anchors in hand-written modules (Access, Identity): the catalog list end, the Add method's return. */
export const CATALOG_LIST_END = /^([ \t]*)\];/m;
export const REGISTRATION_RETURN = /^([ \t]*)return services;/m;
export const CLASS_BODY_END = /^\}\s*$/m;

/** Generated file locations inside a module (relative to the module folder or its tests folder). */
export const ModuleFiles = Object.freeze({
  permissionKeys: (module) => `Application/${module}PermissionKeys.cs`,
  permissionCatalog: (module) => `Application/${module}PermissionCatalogSource.cs`,
  errorCodes: (module) => `Application/${module}ErrorCodes.cs`,
  registration: (module) => `Infrastructure/${module}ModuleRegistration.cs`,
  project: (module) => `${SOLUTION_TOKEN}.Modules.${module}.csproj`,
  testProject: (module) => `${SOLUTION_TOKEN}.Modules.${module}.Tests.csproj`,
  /** The module's documentation (Purpose, Use, Extend, Rules, Gotchas) and its rules file. */
  doc: 'MODULE.md',
  rules: 'module.rules.yaml',
});

/** Downstream generators, run after a scaffold when they exist. */
export const RULES_COMPILE_SCRIPT = 'tools/rules-engine/compile.mjs';
export const DOCSGEN_PROJECT = 'backend/tools/Starter.DocsGen/Starter.DocsGen.csproj';
export const DOCS_GENERATED_DIR = 'docs/generated';
export const OPENAPI_DOCUMENT = 'docs/generated/openapi.v1.json';
export const FRONTEND_WORKSPACE = 'frontend/starter-web';
export const FRONTEND_MANIFEST = 'frontend/starter-web/package.json';
export const API_GEN_SCRIPT = 'api:gen';
export const NODE_MODULES_DIR = 'node_modules';

/** The private toolchain (phase-log): CODELAB_DOTNET wins, then the private SDK, then dotnet on PATH. */
export const DOTNET_ENV = 'CODELAB_DOTNET';
export const PRIVATE_DOTNET = 'E:\\codelab-tools\\dotnet10\\dotnet.exe';
export const DOTNET_ON_PATH = 'dotnet';
export const NPM_COMMAND = process.platform === 'win32' ? 'npm.cmd' : 'npm';
export const MAX_PROCESS_OUTPUT_BYTES = 256 * 1024 * 1024;

/** CLI flags. */
export const Flags = Object.freeze({
  root: '--root',
  name: '--name',
  module: '--module',
  method: '--method',
  route: '--route',
  action: '--action',
  permission: '--permission',
  description: '--description',
  skipGenerate: '--skip-generate',
  noGenerate: '--no-gen',
  dryRun: '--dry-run',
  help: '--help',
  type: '--type',
  codes: '--codes',
  nameEn: '--name-en',
  nameAr: '--name-ar',
  namesEn: '--names-en',
  namesAr: '--names-ar',
});

/** Environment switch that skips the downstream generators (unit tests only). */
export const SKIP_GENERATE_ENV = 'CODELAB_SCAFFOLD_SKIP_GENERATE';

/** Output line prefixes: every CLI prints the files it created or changed. */
export const Report = Object.freeze({
  created: 'created',
  changed: 'changed',
  unchanged: 'unchanged',
  ran: 'ran',
  skipped: 'skipped',
  /** A command the user runs next (new-permission --no-gen). */
  next: 'next:',
  /** The frontend constant a new permission key gets from api:gen. */
  frontendKey: 'frontend key:',
  /** A fact the user should know (new-lookup: no i18n or permission key is needed). */
  note: 'note:',
  /** --dry-run: a file the run would create or change. */
  wouldCreate: 'would create',
  wouldChange: 'would change',
});

/**
 * The recipe each generator lists in the module's module.rules.yaml `recipes` (shown in the generated nested
 * CLAUDE.md), so the next change of the same kind starts from the right document. Each must exist (rules validate).
 */
export const Recipes = Object.freeze({
  endpoint: 'docs/recipes/add-endpoint.md',
  permission: 'docs/recipes/add-permission.md',
  lookup: 'docs/recipes/add-lookup.md',
  transition: '.claude/skills/new-transition/SKILL.md',
});

/** The module.rules.yaml shapes the generators edit (block keys at column 0, a one-line `recipes: [...]`). */
export const RulesYaml = Object.freeze({
  recipesLine: /^recipes:[ \t]*\[(.*)\][ \t]*$/m,
  recipesKey: 'recipes',
  requiredKey: 'required',
  /** A top-level key line: it ends the block above it. */
  topLevelKey: /^[A-Za-z][A-Za-z0-9]*:/,
  listSeparator: ', ',
  itemIndent: '  ',
  fieldIndent: '    ',
});

/** The optional marker a MODULE.md may carry to place the generators' notes; else they go at the end of "Use". */
export const MODULE_DOC_MARKER = '<!-- scaffold:use -->';

/** new-lookup (module 4, Lookups): where a lookup's constants and its seed migration live. */
export const LOOKUP_TEMPLATE_SET = 'lookup';
export const LOOKUP_DOMAIN_DIR = 'backend/src/Starter.Domain/Lookups';
export const LOOKUP_TYPES_FILE = 'backend/src/Starter.Domain/Lookups/LookupTypes.cs';
export const LOOKUP_MIGRATIONS_DIR = 'backend/src/Modules/Lookups/Infrastructure/Persistence/Migrations';
/** The constants class of a lookup's ids (ContactTopic -> ContactTopicIds) and its file. */
export const LOOKUP_IDS_SUFFIX = 'Ids';
/** The seed migration class of a lookup type (contact-topics -> AddContactTopicsLookup). */
export const lookupMigrationClass = (typePascal) => `Add${typePascal}Lookup`;
/** A migration id: a 14-digit UTC timestamp (yyyyMMddHHmmss), an underscore and the class name. */
export const MIGRATION_ID_PATTERN = /"(\d{14})_[A-Za-z][A-Za-z0-9]*"/g;
/** A new seed migration sorts this many minutes after the newest Lookups migration (deterministic, no clock). */
export const MIGRATION_STEP_MINUTES = 1;
/** Mirrors Starter.Domain.Lookups.LookupLimits. */
export const LOOKUP_KEY_MAX_LENGTH = 64;
export const LOOKUP_NAME_MAX_LENGTH = 200;
/** The most values one new-lookup run seeds; a longer list is a hand-written migration. */
export const MAX_LOOKUP_CODES = 100;
/** The Lookups module: its docs (seeded-types table) and its tests (the migration list, one seed test per type). */
export const LOOKUP_MODULE_DIR = 'backend/src/Modules/Lookups';
export const LOOKUP_SEEDED_TYPES_HEADER = '| Type | Ids | Values |';
export const LOOKUP_TESTS_PROJECT = 'backend/tests/Modules/Starter.Modules.Lookups.Tests';
export const LOOKUP_MODEL_TESTS_FILE = 'backend/tests/Modules/Starter.Modules.Lookups.Tests/Infrastructure/PersistenceTests.cs';
/** The model test's exact migration list: `[LookupsMigrationIds.InitialCreate, ...], context.Database.GetMigrations()`. */
export const LOOKUP_MIGRATION_LIST = /(\[LookupsMigrationIds\.InitialCreate[^\]]*)(\],\s*context\.Database\.GetMigrations\(\)\))/;
/** The gap between seeded sort orders (10, 20, 30, ...), leaving room to insert a value between two. */
export const LOOKUP_SORT_STEP = 10;
/** Separators of the list arguments: codes by comma, display names by semicolon (names may hold commas). */
export const CODE_LIST_SEPARATOR = ',';
export const NAME_LIST_SEPARATOR = ';';

export const UTF8 = 'utf8';
export const NEWLINE = '\n';
export const CRLF = '\r\n';
