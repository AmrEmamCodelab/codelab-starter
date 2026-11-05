// Every name, path, port, timeout and message of run-local (tools/run-local, run-local.ps1). No other file in
// tools/run-local holds a literal of these. Ports and URLs are the contract's local ports (phase 2 contract 7).

/** Process exit codes (the same contract as the other repo CLIs). */
export const ExitCode = Object.freeze({
  ok: 0,
  /** Refused or failed: already running, a missing prerequisite, a failed start or health check. */
  failed: 1,
  usage: 2,
});

/** Local endpoints. */
export const Urls = Object.freeze({
  api: 'http://localhost:5180',
  web: 'http://localhost:4280',
  seqDefault: 'http://localhost:5341',
});
/** Codelab.Observability HealthEndpoints.ReadyPath. */
export const API_HEALTH_PATH = '/health/ready';
export const WEB_HEALTH_PATH = '/';
export const HTTP_OK = 200;

/** Repo paths. */
export const API_PROJECT_DIR = 'backend/src/Starter.Api';
export const API_PROJECT_FILE = 'backend/src/Starter.Api/Starter.Api.csproj';
/** The API is built first, then its assembly runs detached (a detached `dotnet run` exits at once on Windows). */
export const API_BUILD_OUTPUT_DIR = 'backend/src/Starter.Api/bin/Debug';
export const API_ASSEMBLY = 'Starter.Api.dll';
export const URLS_ENV = 'ASPNETCORE_URLS';
export const LOCAL_SECRETS_FILE = 'backend/src/Starter.Api/appsettings.Development.local.json';
export const SECRETS_SCRIPT = 'tools/devseed/New-LocalSecrets.ps1';
export const FRONTEND_WORKSPACE = 'frontend/starter-web';
export const FRONTEND_MANIFEST = 'frontend/starter-web/package.json';
export const FRONTEND_START_SCRIPT = 'start';
export const NODE_MODULES_DIR = 'node_modules';

/** The private toolchain (phase-log): environment overrides first. */
export const DOTNET_ENV = 'CODELAB_DOTNET';
export const PRIVATE_DOTNET = 'E:\\codelab-tools\\dotnet10\\dotnet.exe';
export const DOTNET_ON_PATH = 'dotnet';
export const NODE_DIR_ENV = 'CODELAB_NODE_DIR';
export const PRIVATE_NODE_DIR = 'E:\\codelab-tools\\node24';
export const NPM_SHIM = 'npm.cmd';
export const CMD = 'cmd.exe';
export const POWERSHELL = 'powershell.exe';
export const POWERSHELL_FILE_ARGUMENTS = Object.freeze(['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File']);
export const TASKKILL = 'taskkill.exe';
export const TASKLIST = 'tasklist.exe';
export const SQL_LOCALDB = 'sqllocaldb';
export const LOCALDB_INSTANCE = 'MSSQLLocalDB';
export const SERVICE_CONTROL = 'sc.exe';
export const SEQ_SERVICE = 'Seq';
export const PATH_ENV = 'PATH';

/** Seq: CODELAB_SEQ_URL wins; the API reads Observability:Seq:ServerUrl. */
export const SEQ_URL_ENV = 'CODELAB_SEQ_URL';
export const SEQ_SERVER_URL_ENV = 'Observability__Seq__ServerUrl';
export const ENVIRONMENT_ENV = 'ASPNETCORE_ENVIRONMENT';
export const DEVELOPMENT = 'Development';

/** Where run-local records what it started: %TEMP%\codelab-starter. */
export const STATE_DIR_NAME = 'codelab-starter';
export const STATE_FILE = 'run-local.json';
export const LOG_FILES = Object.freeze({ api: 'api.log', web: 'web.log' });

/** The processes run-local starts, with the image name each recorded PID must still have before -Stop kills it. */
export const Services = Object.freeze({
  api: { name: 'api', image: 'dotnet.exe' },
  web: { name: 'web', image: 'cmd.exe' },
});

/** Timeouts (milliseconds). The first API start builds the solution; the first web start builds the bundle. */
export const Timeouts = Object.freeze({
  api: 4 * 60 * 1000,
  web: 6 * 60 * 1000,
  probe: 3 * 1000,
  pollInterval: 2 * 1000,
});
export const LOG_TAIL_CHARACTERS = 4000;

/** CLI flags (run-local.ps1 maps -Stop, -NoWeb, -NoSeq onto them). */
export const Flags = Object.freeze({ stop: '--stop', noWeb: '--no-web', noSeq: '--no-seq', root: '--root', help: '--help' });
export const USAGE = 'Usage: node tools/run-local/run-local.mjs [--stop] [--no-web] [--no-seq] [--root <dir>]';

export const UTF8 = 'utf8';
