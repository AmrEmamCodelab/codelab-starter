// Runs tools/devseed/Initialize-LocalDatabase.ps1 the way init.ps1 does (Windows PowerShell 5.1). The argument and
// refusal rules run without SQL Server; the end-to-end run (build, migrate, break-glass, rerun skip) runs only when
// CODELAB_DEVSEED_SQL names a throwaway local server, e.g. "Server=(localdb)\P2Proof;Integrated Security=true".
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { runScript, makeTempDir } from '../../fork/tests/support/powershell.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(HERE, '..', '..', '..');
const SCRIPT = join(REPO_ROOT, 'tools', 'devseed', 'Initialize-LocalDatabase.ps1');
const SECRETS_SCRIPT = join(REPO_ROOT, 'tools', 'devseed', 'New-LocalSecrets.ps1');
const SQL_ENV = 'CODELAB_DEVSEED_SQL';
const LOCAL_SERVER = 'Server=(localdb)\\P2Proof;Integrated Security=true';
const EXIT_DONE = 0;
const EXIT_REFUSED = 1;
const EXIT_USAGE = 2;
const DATABASE_SUFFIX_BYTES = 4;
const LIVE_TIMEOUT_MS = 15 * 60 * 1000;
const ONE_TIME_PASSWORD = /One-time break-glass password[^\n]*\): (\S+)/;
const SECRETS_FILE_NAME = 'appsettings.Development.local.json';
const LOCAL_JSON_SUFFIX = '.local.json';
const CONNECTION_SECTION = 'ConnectionStrings';
const CONNECTION_NAME = 'Starter';
const QUICK_TIMEOUT_SECONDS = '20';
const MINIMAL_SECRETS = { Identity: { BreakGlass: { UserName: 'breakglass' } } };

function run(args) {
  return runScript(SCRIPT, args, { cwd: REPO_ROOT });
}

function withTemp(body) {
  const temp = makeTempDir();
  try {
    return body(temp.path);
  } finally {
    temp.cleanup();
  }
}

test('-ConnectionString is required', () => {
  const result = run([]);
  assert.equal(result.code, EXIT_USAGE, result.output);
  assert.match(result.output, /-ConnectionString is required/);
});

test('a database name outside the safe pattern is a usage error', () => {
  const result = run(['-ConnectionString', LOCAL_SERVER, '-DatabaseName', 'bad-name;drop']);
  assert.equal(result.code, EXIT_USAGE, result.output);
});

test('a server that is not on this machine is refused before anything is touched', () => {
  const result = run(['-ConnectionString', 'Server=sql.example.test,1433;Database=StarterDev;Integrated Security=true']);
  assert.equal(result.code, EXIT_REFUSED, result.output);
  assert.match(result.output, /not a SQL Server on this machine/);
});

test('without the local secrets file it is refused and points at New-LocalSecrets.ps1', () => {
  withTemp((base) => {
    const result = run(['-ConnectionString', LOCAL_SERVER, '-DatabaseName', 'StarterDev', '-SecretsPath', join(base, 'absent.json')]);
    assert.equal(result.code, EXIT_REFUSED, result.output);
    assert.match(result.output, /New-LocalSecrets\.ps1/);
  });
});

/** Drops a throwaway database (the test's own, never another). */
function dropDatabase(folder, connectionString, name) {
  const dropScript = join(folder, 'drop.ps1');
  writeFileSync(dropScript, [
    'param([string] $Connection, [string] $Name)',
    '$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $Connection',
    "$builder['Initial Catalog'] = 'master'",
    '$c = New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString; $c.Open(); $cmd = $c.CreateCommand()',
    "$cmd.CommandText = \"IF DB_ID(@n) IS NOT NULL BEGIN DECLARE @s nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(@n) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(@n); EXEC (@s) END\"",
    "[void] $cmd.Parameters.AddWithValue('@n', $Name); [void] $cmd.ExecuteNonQuery(); $c.Dispose()",
  ].join('\n'));
  runScript(dropScript, ['-Connection', connectionString, '-Name', name]);
}

test('creates a database with every migration and a break-glass password printed once; a rerun skips', { skip: !process.env[SQL_ENV], timeout: LIVE_TIMEOUT_MS }, () => {
  const connectionString = process.env[SQL_ENV];
  const database = `DevSeedTest_${randomBytes(DATABASE_SUFFIX_BYTES).toString('hex')}`;
  withTemp((base) => {
    const folders = ['api', 'service', 'gateway', 'ops'].map((name) => join(base, name));
    folders.forEach((folder) => mkdirSync(folder));
    const [api, service, gateway, ops] = folders.map((folder) => join(folder, 'appsettings.Development.local.json'));
    assert.equal(runScript(SECRETS_SCRIPT, ['-Path', api, '-ServicePath', service, '-GatewayPath', gateway, '-OpsPath', ops, '-StorageRoot', join(base, 'store')], { cwd: REPO_ROOT }).code, EXIT_DONE);
    const args = ['-ConnectionString', connectionString, '-DatabaseName', database, '-SecretsPath', api, '-ArtifactsPath', join(base, 'artifacts')];
    try {
      const created = run(args);
      assert.equal(created.code, EXIT_DONE, created.output);
      const [, password] = created.output.match(ONE_TIME_PASSWORD) ?? [];
      assert.ok(password, 'the one-time password is printed');
      assert.equal(JSON.parse(readFileSync(api, 'utf8')).Identity.BreakGlass.Password, password, 'and saved as the configured password');
      const again = run(args);
      assert.equal(again.code, EXIT_DONE, again.output);
      assert.match(again.output, /already holds the break-glass administrator; skipped/);
      assert.doesNotMatch(again.output, ONE_TIME_PASSWORD, 'never printed twice');
    } finally {
      dropDatabase(base, connectionString, database);
    }
  });
});

/** Every file under `folder` (recursive); empty when the folder does not exist. */
function filesUnder(folder) {
  if (!existsSync(folder)) return [];
  return readdirSync(folder, { recursive: true, withFileTypes: true }).filter((entry) => entry.isFile()).map((entry) => join(entry.parentPath, entry.name));
}

/**
 * A stand-in dotnet: `build` leaves Starter.Api.dll where the script looks for it; running the dll exits at once, so
 * the one-shot start fails without a real API.
 */
function writeFakeDotnet(folder) {
  const script = join(folder, 'fake-dotnet.mjs');
  writeFileSync(script, [
    "import { mkdirSync, writeFileSync } from 'node:fs';",
    "import { join } from 'node:path';",
    'const args = process.argv.slice(2);',
    "if (args[0] === 'build') {",
    "  const output = join(args[args.indexOf('--artifacts-path') + 1], 'bin', 'Starter.Api', 'debug');",
    '  mkdirSync(output, { recursive: true });',
    "  writeFileSync(join(output, 'Starter.Api.dll'), '');",
    '  process.exit(0);',
    '}',
    'process.exit(3);',
  ].join('\n'));
  const command = join(folder, 'fake-dotnet.cmd');
  writeFileSync(command, `@"${process.execPath}" "${script}" %*\r\n`);
  return command;
}

/** Runs one SQL batch against `database` (created when missing) on the test's own server. */
function runSql(folder, connectionString, database, sql) {
  const sqlScript = join(folder, 'sql.ps1');
  writeFileSync(sqlScript, [
    'param([string] $Connection, [string] $Name, [string] $Sql)',
    '$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $Connection',
    "$builder['Initial Catalog'] = 'master'",
    '$c = New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString; $c.Open(); $cmd = $c.CreateCommand()',
    "$cmd.CommandText = \"IF DB_ID(@n) IS NULL BEGIN DECLARE @s nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@n); EXEC (@s) END\"",
    "[void] $cmd.Parameters.AddWithValue('@n', $Name); [void] $cmd.ExecuteNonQuery(); $c.Dispose()",
    "$builder['Initial Catalog'] = $Name",
    '$c = New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString; $c.Open(); $cmd = $c.CreateCommand()',
    '$cmd.CommandText = $Sql; [void] $cmd.ExecuteNonQuery(); $c.Dispose()',
  ].join('\n'));
  const result = runScript(sqlScript, ['-Connection', connectionString, '-Name', database, '-Sql', sql]);
  assert.equal(result.code, EXIT_DONE, result.output);
}

function writeSecrets(folder) {
  const file = join(folder, SECRETS_FILE_NAME);
  writeFileSync(file, JSON.stringify(MINIMAL_SECRETS));
  return file;
}

test('a failed one-shot start leaves no copy of the local secrets file in the artifacts folder', { skip: !process.env[SQL_ENV] }, () => {
  const connectionString = process.env[SQL_ENV];
  const database = `DevSeedTest_${randomBytes(DATABASE_SUFFIX_BYTES).toString('hex')}`;
  withTemp((base) => {
    const secrets = writeSecrets(base);
    const artifacts = join(base, 'artifacts');
    try {
      const result = run(['-ConnectionString', connectionString, '-DatabaseName', database, '-SecretsPath', secrets, '-ArtifactsPath', artifacts,
        '-Dotnet', writeFakeDotnet(base), '-TimeoutSeconds', QUICK_TIMEOUT_SECONDS]);
      assert.equal(result.code, EXIT_REFUSED, result.output);
      assert.ok(filesUnder(artifacts).some((file) => file.endsWith('Starter.Api.dll')), 'the fake build ran');
      assert.deepEqual(filesUnder(artifacts).filter((file) => file.endsWith(LOCAL_JSON_SUFFIX)), [], 'no secrets copy is left behind');
      assert.equal(JSON.parse(readFileSync(secrets, 'utf8'))[CONNECTION_SECTION], undefined, 'a failed run saves no connection string');
    } finally {
      dropDatabase(base, connectionString, database);
    }
  });
});

test('an already-initialized database is skipped and its connection string is saved in the local secrets file', { skip: !process.env[SQL_ENV] }, () => {
  const connectionString = process.env[SQL_ENV];
  const database = `DevSeedTest_${randomBytes(DATABASE_SUFFIX_BYTES).toString('hex')}`;
  withTemp((base) => {
    const secrets = writeSecrets(base);
    try {
      runSql(base, connectionString, database,
        "EXEC (N'CREATE SCHEMA [identity]'); CREATE TABLE [identity].[UserAccounts] ([IsBreakGlass] bit NOT NULL); INSERT INTO [identity].[UserAccounts] VALUES (1);");
      const result = run(['-ConnectionString', connectionString, '-DatabaseName', database, '-SecretsPath', secrets]);
      assert.equal(result.code, EXIT_DONE, result.output);
      assert.match(result.output, /already holds the break-glass administrator; skipped/);
      const saved = JSON.parse(readFileSync(secrets, 'utf8'));
      assert.match(saved[CONNECTION_SECTION][CONNECTION_NAME], new RegExp(`Initial Catalog=${database}`), 'the init database is the one the API uses');
      assert.deepEqual(saved.Identity, MINIMAL_SECRETS.Identity, 'the other secrets are kept');
    } finally {
      dropDatabase(base, connectionString, database);
    }
  });
});
