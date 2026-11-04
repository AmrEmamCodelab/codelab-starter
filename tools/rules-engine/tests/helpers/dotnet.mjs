// Runs the .NET 10 SDK the same way tools/build.ps1 resolves it.
import { existsSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { REPO_ROOT, writeWorkspaceFile } from './workspace.mjs';

const PRIVATE_DOTNET = 'E:\\codelab-tools\\dotnet10\\dotnet.exe';
const PATH_DOTNET = 'dotnet';
const BUILD_TIMEOUT_MS = 5 * 60 * 1000;
const OUTPUT_BUFFER_BYTES = 64 * 1024 * 1024;

/** $env:CODELAB_DOTNET, else the private .NET 10 install when present, else dotnet on PATH. */
export function resolveDotnet() {
  if (process.env.CODELAB_DOTNET) {
    return process.env.CODELAB_DOTNET;
  }
  return existsSync(PRIVATE_DOTNET) ? PRIVATE_DOTNET : PATH_DOTNET;
}

const importRepoFile = (fileName) =>
  `<Project>\n  <Import Project="${path.join(REPO_ROOT, fileName)}" />\n</Project>\n`;

/**
 * Makes a temp workspace build like the repo: Directory.Build.props/.targets and Directory.Packages.props import the
 * real repo files (Codelab.Build.Sdk from source, central versions), and nuget.config uses nuget.org only.
 */
export function wireWorkspaceToRepoBuild(root) {
  for (const fileName of ['Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props']) {
    writeWorkspaceFile(root, fileName, importRepoFile(fileName));
  }
  writeWorkspaceFile(root, 'nuget.config', [
    '<?xml version="1.0" encoding="utf-8"?>',
    '<configuration>',
    '  <packageSources>',
    '    <clear />',
    '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />',
    '  </packageSources>',
    '</configuration>',
    '',
  ].join('\n'));
}

/** dotnet build <project> -warnaserror; returns { status, output }. */
export function dotnetBuild(projectPath) {
  const result = spawnSync(resolveDotnet(), ['build', projectPath, '-warnaserror', '-nologo', '-v:minimal'], {
    cwd: path.dirname(projectPath),
    encoding: 'utf8',
    timeout: BUILD_TIMEOUT_MS,
    maxBuffer: OUTPUT_BUFFER_BYTES,
    env: { ...process.env, DOTNET_CLI_TELEMETRY_OPTOUT: '1', DOTNET_NOLOGO: '1' },
  });
  return { status: result.status, output: `${result.stdout ?? ''}${result.stderr ?? ''}${result.error ?? ''}` };
}
