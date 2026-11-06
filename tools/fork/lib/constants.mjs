// Every name, path, limit and message key of the fork-model tools (init.ps1, tools/remove-module.ps1,
// tools/upstream-merge.ps1, tools/fork/Test-ForkModel.ps1). No other file in tools/fork holds a literal of these.
//
// RENAME SAFETY (ADR-003): the starter-named literals below (SOLUTION_TOKEN, SOLUTION_FILE, API_PROJECT, ...) are
// written in the form a fork's renameMap finds, so init's full rename rewrites them together with the files they
// name. A fork's copy of this file therefore always names the fork's own paths. Never build these names from parts.

/** Process exit codes shared by every fork CLI. */
export const ExitCode = Object.freeze({
  /** Success; for upstream-merge: merged clean. */
  ok: 0,
  /** Refused (nothing changed), conflicts left for the human, a failed build or a failed assertion. */
  failed: 1,
  /** Usage or internal error. */
  usage: 2,
});

/** The fork manifest at the repo root, and the manifest's own schema. "codelab.starter" is rename-protected. */
export const MANIFEST_FILE = 'codelab.starter.json';
export const MANIFEST_SCHEMA_FILE = 'tools/fork/codelab.starter.schema.json';
export const MANIFEST_SCHEMA_VERSION = 1;
/**
 * Names a renameMap must never rewrite: the manifest's file name and its field names (a fork's tools read the same
 * manifest format the starter writes). init adds an identity entry for each when it applies a rename.
 */
export const RENAME_PROTECTED_NAMES = Object.freeze(['codelab.starter', 'starterBase', 'starterVersion']);

/** The solution token every starter project, namespace and assembly name derives from. */
export const SOLUTION_TOKEN = 'Starter';
export const SOLUTION_FILE = 'codelab-starter.slnx';
export const API_PROJECT = 'backend/src/Starter.Api/Starter.Api.csproj';
/** Kernel project names (Starter.<Name>); remove-module refuses them. */
export const KERNEL_PROJECTS = Object.freeze(['Domain', 'Application', 'Infrastructure', 'Api']);

/** Where a module lives (ADR-004). {name} is the PascalCase module name, {kebab} its kebab-case form. */
export const MODULE_PATHS = Object.freeze({
  backend: 'backend/src/Modules/{name}/',
  tests: 'backend/tests/Modules/Starter.Modules.{name}.Tests/',
  frontend: 'frontend/starter-web/src/app/features/{kebab}/',
});
/** Project file names of a module and of its tests; used to find their lines in the solution and host project. */
export const MODULE_PROJECT_FILE = 'Starter.Modules.{name}.csproj';
export const MODULE_TEST_PROJECT_FILE = 'Starter.Modules.{name}.Tests.csproj';
export const NAME_PLACEHOLDER = '{name}';
export const KEBAB_PLACEHOLDER = '{kebab}';
export const MODULE_NAME_PATTERN = /^[A-Z][A-Za-z0-9]*$/;

/**
 * Module marker comments: a registration of a module in a shared file carries `// codelab:module <Name>` (or
 * `<!-- codelab:module <Name> -->`) on the same line, or sits between `codelab:module-begin <Name>` and
 * `codelab:module-end <Name>` lines. remove-module deletes those lines.
 */
export const MODULE_MARKER = Object.freeze({
  line: 'codelab:module',
  begin: 'codelab:module-begin',
  end: 'codelab:module-end',
  /** Comment openers a marker may follow: `//` (TypeScript, C#) and `<!--` (csproj, HTML). */
  commentOpeners: Object.freeze(['//', '<!--']),
  htmlCommentClose: '-->',
});
/**
 * Where remove-module looks for marker lines: every file with one of the extensions under one of the roots
 * (repo-relative, '/' separators). The registration files (app.routes.ts, app-nav.ts, app.config.ts,
 * features/admin/admin.routes.ts, Starter.Api.csproj) are all inside them.
 */
export const MARKER_SCAN_ROOTS = Object.freeze(['frontend/starter-web/src/', 'backend/src/']);
export const MARKER_SCAN_EXTENSIONS = Object.freeze(['.ts', '.html', '.csproj']);
/** Folders a marker scan never enters (build outputs and installed packages). */
export const MARKER_SCAN_SKIPPED_DIRS = Object.freeze(['node_modules', 'bin', 'obj', 'dist', '.angular']);
/** The app's translation files; remove-module deletes the module's `<camelName>.` keys from each one present. */
export const I18N_FILES = Object.freeze(['frontend/starter-web/src/assets/i18n/ar.json', 'frontend/starter-web/src/assets/i18n/en.json']);
export const I18N_KEY_SEPARATOR = '.';

/**
 * The deletability probe (tools/ci/module-deletability.ps1 -DummyModule, in a temp copy only): a scaffolded module
 * gets a marker-registered frontend route, a marked host-project block and translation keys, so the removal of every
 * registration kind is proven before a real sample module exists.
 */
export const DELETABILITY_PROBE = Object.freeze({
  appRoutesFile: 'frontend/starter-web/src/app/app.routes.ts',
  /** The line the probe's route spread goes under (the app's top-level route list). */
  routesAnchor: 'export const routes: Routes = [',
  routesFileSuffix: '.routes.ts',
  /** The import specifier suffix of the routes file (no extension). */
  routesImportSuffix: '.routes',
  routesConstSuffix: '_PROBE_ROUTES',
  translationKey: 'probe',
  translation: Object.freeze({ en: 'Deletability probe', ar: 'مسبار قابلية الحذف' }),
  i18nLanguageFile: /\/(en|ar)\.json$/,
  projectClose: '</Project>',
  itemGroupLabel: 'deletability probe',
});

/** Files that list modules line by line; upstream-merge drops the lines of removed modules from their patches. */
export const MODULE_LIST_FILES = Object.freeze([SOLUTION_FILE, API_PROJECT]);

/**
 * Outputs of tools/rules-engine/compile.mjs: upstream-merge never merges them, it regenerates them for the fork's
 * own module set. The per-module files count only next to a module.rules.yaml (the repo root CLAUDE.md is source).
 */
export const RULES_OUTPUTS = Object.freeze(['rules/rules.compiled.json', 'rules/eslint-boundaries.compiled.json', 'rules/codelab.globalconfig']);
export const MODULE_RULES_FILE = 'module.rules.yaml';
export const MODULE_RULES_OUTPUTS = Object.freeze(['CLAUDE.md', 'BannedSymbols.generated.txt', '.editorconfig']);

/** Package source and the local feed (ADR-002). */
export const PACKAGES_DIR = 'packages/';
export const VERSIONS_FILE = 'packages/versions.json';
export const FEED_DIR = 'feed';
export const FEED_NUGET_DIR = 'nuget';
export const FEED_NPM_DIR = 'npm';
export const NUGET_EXTENSION = '.nupkg';
export const NPM_EXTENSION = '.tgz';
export const NPM_SCOPE = '@codelab/';
export const NPM_SCOPE_FILE_PREFIX = 'codelab-';
export const CI_PRERELEASE_LABEL = 'ci';
export const BUILD_SDK_ID = 'Codelab.Build.Sdk';
export const NPM_FILE_SPEC_PREFIX = 'file:';

/** Repo-wide build files init switches to feed mode. */
export const BUILD_PROPS_FILE = 'Directory.Build.props';
export const PACKAGES_PROPS_FILE = 'Directory.Packages.props';
export const PACKAGE_SOURCE_SWITCH = 'UseCodelabPackageSource';
export const BUILD_SDK_VERSION_PROPERTY = 'CodelabBuildSdkVersion';
export const FEED_PIN_ITEM_GROUP_LABEL = 'Codelab packages (feed only, pinned by init.ps1)';

/** npm manifests whose @codelab/* dependencies init points at the fork's feed tarballs. */
export const ROOT_NPM_MANIFEST = 'package.json';
export const ROOT_NPM_LOCK = 'package-lock.json';
export const FRONTEND_NPM_MANIFEST = 'frontend/starter-web/package.json';
export const NPM_MANIFESTS = Object.freeze([ROOT_NPM_MANIFEST, FRONTEND_NPM_MANIFEST]);
export const NPM_DEPENDENCY_FIELDS = Object.freeze(['dependencies', 'devDependencies', 'optionalDependencies', 'peerDependencies']);
/** The root workspace glob that points at package source; a fork has no packages/, so init drops it. */
export const PACKAGE_WORKSPACE_PREFIX = 'packages/';

/** Scripts the fork tools call. */
export const REMOVE_MODULE_SCRIPT = 'tools/remove-module.ps1';
export const UPSTREAM_MERGE_SCRIPT = 'tools/upstream-merge.ps1';
export const INIT_SCRIPT = 'init.ps1';
export const LOCAL_SECRETS_SCRIPT = 'tools/devseed/New-LocalSecrets.ps1';
export const RULES_COMPILE_SCRIPT = 'tools/rules-engine/compile.mjs';
export const BUILD_SCRIPT = 'tools/build.ps1';
export const BUILD_SCRIPT_ARGUMENTS = Object.freeze(['-Target', 'CI']);
export const SCAFFOLD_MODULE_SCRIPT = 'tools/scaffold/new-module.mjs';
export const PACKAGE_VERSIONS_SCRIPT = 'tools/ci/package-versions.mjs';

/** The product prefix (Codelab.Security SecurityProtocol.IsValidPrefix) and the prefixes a fork may not take. */
export const PRODUCT_PREFIX_PATTERN = /^[A-Z][A-Z0-9]{1,15}$/;
export const RESERVED_PREFIXES = Object.freeze(['STARTER', 'CODELABTEST', 'ALTPFX']);
/** prefixTargets[].jsonPath separators: ':' (.NET configuration keys) or '.' (JSON paths). */
export const JSON_PATH_SEPARATOR = /[:.]/;

/** Git. */
export const GIT = 'git';
/** A git work tree's metadata (a folder, or a file in a linked worktree); the deletability probe refuses a root that has one. */
export const GIT_METADATA = '.git';
export const DEFAULT_UPSTREAM_BRANCH = 'main';
export const FETCH_HEAD = 'FETCH_HEAD';
export const NULL_BLOB = /^0+$/;
export const INIT_COMMIT_TEMPLATE = 'init: minimal fork (prefix {prefix}, removed {removed})';
export const NO_REMOVED_MODULES = 'none';
export const CONFLICT_MARKER_PATTERN = /^(?:<{7}|>{7})(?: |$)/m;
export const MAX_PROCESS_OUTPUT_BYTES = 512 * 1024 * 1024;

/** Windows PowerShell (the scripts' baseline, 5.1) as the fork tools run it. */
export const POWERSHELL = 'powershell.exe';
export const POWERSHELL_ARGUMENTS = Object.freeze(['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass']);
export const NPM_COMMAND = process.platform === 'win32' ? 'npm.cmd' : 'npm';
export const NPM_LOCK_REFRESH_ARGUMENTS = Object.freeze(['install', '--package-lock-only', '--ignore-scripts', '--no-audit', '--no-fund']);

/** The fork-model test (Test-ForkModel.ps1): defaults it uses when the caller passes none. */
export const FORK_MODEL = Object.freeze({
  productPrefix: 'FORKTEST',
  editFiles: Object.freeze(['CONTEXT.md', 'docs/README.md', 'tools/README.md', 'docs/adr/README.md', 'docs/standards/SKILL.md']),
  addFile: 'docs/fork-model-probe.md',
  moduleProbeFile: 'fork-model-probe.md',
  packagesProbeFile: 'packages/fork-model-probe.md',
  moduleEditCandidates: Object.freeze(['MODULE.md']),
  editCount: 5,
  branch: DEFAULT_UPSTREAM_BRANCH,
  upstreamLabel: 'upstream',
  forkLabel: 'fork',
  userName: 'Codelab Fork Model',
  userEmail: 'fork-model@example.test',
  tempPrefix: 'codelab-fork-model-',
  ciNumber: 1,
  scratch: Object.freeze({
    upstream: 'upstream',
    fork: 'fork',
    pack: 'pack',
    nugetPackages: 'nuget-packages',
    mergeReport: 'merge-report.json',
    continueReport: 'continue-report.json',
  }),
  commits: Object.freeze({
    snapshot: 'starter snapshot with module',
    upstream: 'upstream: fork-model probe change',
    forkEdit: 'fork: local edit',
    merge: 'merge: starter upstream (fork-model probe)',
  }),
  skippedDirs: Object.freeze(['.git', 'node_modules']),
});
export const DOTNET_ENV = 'CODELAB_DOTNET';
export const PRIVATE_DOTNET = 'E:\\codelab-tools\\dotnet10\\dotnet.exe';
export const DOTNET = 'dotnet';
export const NUGET_PACKAGES_ENV = 'NUGET_PACKAGES';
export const FEED_MODE_PROPERTY = '-p:UseCodelabPackageSource=false';
export const PROJECT_ASSETS_FILE = 'project.assets.json';
export const NUPKG_METADATA_FILE = '.nupkg.metadata';
export const OBJ_DIR = 'obj';
export const PACK_SWITCH = '-Pack';

/** Environment variables the PowerShell wrappers use to hand free text to node (PS 5.1 mangles quoted arguments). */
export const BUILD_COMMAND_ENV = 'CODELAB_FORK_BUILD_COMMAND';

/** Text files are hashed and renamed; a NUL byte in the first block marks a file as binary (git's own heuristic). */
export const BINARY_SNIFF_BYTES = 8000;
export const NUL_BYTE = 0;
export const HASH_ALGORITHM = 'sha256';
export const HASH_ENCODING = 'hex';
/** Paths never hashed as source-owned (the manifest itself and the gitignored feed). */
export const HASH_EXCLUDED_PREFIXES = Object.freeze([MANIFEST_FILE, `${FEED_DIR}/`]);

/** JSON output of manifests and reports. */
export const JSON_INDENT = 2;
export const UTF8_BOM = '﻿';
