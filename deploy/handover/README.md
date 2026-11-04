# deploy/handover

## Purpose

The client handover kit (decision D7, [ADR-008](../../docs/adr/ADR-008-handover-kit.md)). A kit is the **full
dependency closure** a client needs to restore and build the delivered source with no network access:

| Kit entry | What it holds |
| --- | --- |
| `nuget\` | Every NuGet package the listed projects restore: `Codelab.*` from the feed, every third-party package and MSBuild SDK packages. It is trimmed to a folder feed (`.nupkg`, `.nupkg.sha512`, `.nuspec`, `.nupkg.metadata` per `{id}\{version}`). |
| `nuget.config` | `<clear/>` and one folder source, `./nuget`. There is no nuget.org and no package source mapping. |
| `npm-cache\` | The npm cache after `npm ci --cache` in each npm install folder, holding every Codelab and third-party tarball. |
| `npm-files\` | Tarballs a lock file installs by `file:` path (for example `feed/npm/*.tgz`), stored at their source-relative path. |
| `OFFLINE.md` | The offline restore commands, including `npm ci --offline --cache ./npm-cache`. |
| `sources\` | One zip per Codelab package folder (`packages\dotnet\*`, `packages\npm\*`, `packages\vectors`). Entry names are source-relative and contain no build output. |
| `PREREQUISITES.md` | The .NET SDK (from `global.json`), Node (from `.nvmrc`) and Playwright browser versions. |
| `manifest.json` | `kind: codelab-handover-kit`, `schemaVersion`, the inputs, the tool versions and the SHA-256 and size of every other file. |

## Use

Prepend the private Node to PATH, or let the scripts find it. They use `CODELAB_NODE_DIR`, then
`E:\codelab-tools\node24`, then the `node` on PATH. dotnet is resolved the same way `tools\build.ps1` does it.

```powershell
# Build a kit (project paths are relative to the repo root; -OutputPath to the current folder)
powershell -NoProfile -ExecutionPolicy Bypass -File deploy\handover\New-HandoverKit.ps1 `
  -OutputPath $env:TEMP\codelab-handover-test `
  -Projects packages\dotnet\Codelab.Security\Codelab.Security.csproj `
  -NpmProjects packages\npm\ng-security

# Prove it offline (exit 0 = closure complete)
powershell -NoProfile -ExecutionPolicy Bypass -File deploy\handover\Test-HandoverKit.ps1 -KitPath $env:TEMP\codelab-handover-test

# Replace a previous kit in the same folder
powershell -NoProfile -ExecutionPolicy Bypass -File deploy\handover\New-HandoverKit.ps1 -OutputPath <kit> -Projects <...> -Force

# Unit tests (no network), and the opt-in end-to-end run against this repo
node --test "deploy/handover/tests/**/*.test.mjs"
$env:CODELAB_HANDOVER_E2E = '1'; node --test "deploy/handover/tests/**/*.test.mjs"
```

`New-HandoverKit.ps1` parameters:

| Parameter | Meaning |
| --- | --- |
| `-OutputPath` | Required. The kit folder. It must not be a drive root and must not be inside the source root or contain it. |
| `-Projects` | `.csproj`, `.slnx` or `.sln` files, relative to `-SourceRoot`. Comma-separated values are accepted. |
| `-NpmProjects` | Folders with a `package.json`. A folder without its own `package-lock.json` must be a workspace member of the nearest ancestor that has one; it is installed from there. |
| `-FeedPath` | The Codelab feed folder (`nuget\`, `npm\`). The default is `feed`. |
| `-MsBuildProperties` | `Name=Value` restore properties, for example `UseCodelabPackageSource=false` for feed mode (ADR-002). They are stored in the manifest and reused by the test. |
| `-SourceRoot` | The default is the repo that holds the script. |
| `-Force` | Replaces a previous kit. See Rules. |

Exit codes for both scripts: `0` success, `1` a restore, build, integrity or step failure, `2` bad arguments or an
unsafe output folder.

`Test-HandoverKit.ps1` runs these steps:

1. It checks every file against `manifest.json` and refuses unlisted files.
2. It makes a clean copy of the source tree in `%TEMP%`. The Codelab feed is not copied, every `nuget.config` in the copy is replaced by the kit's, and `npm-files\` is put back in place.
3. It sets these for every child process:
   - `HTTP_PROXY`, `HTTPS_PROXY`, `ALL_PROXY` and the npm proxy settings point at `http://127.0.0.1:9`, where nothing listens;
   - `NO_PROXY` is cleared;
   - `NUGET_PACKAGES` is a new empty folder.
4. It runs `dotnet restore --configfile <kit>\nuget.config`, then `dotnet build --no-restore`.
5. It runs `npm ci --offline --cache <copy of the kit cache>`, then `npm run build` and `npm test` where those scripts exist (the test imports and runs the installed packages offline).

## Extend

- **Another closure part** (for example installers in P10):
  - add a constant and an entry to `KitOwnedEntries` in `lib/KitConstants.ps1`;
  - write it from `New-HandoverKit.ps1` before the manifest is written (the manifest hashes the whole folder);
  - check it in `Test-HandoverKit.ps1`;
  - add a test under `tests/`.
- **Another source zip root:** add it to `PackageSourceGroups` (each child folder becomes one zip) or to `PackageSourceFolders` (the whole folder becomes one zip).
- **A changed manifest shape:** bump `KitSchemaVersion`. The test refuses other versions.
- **Library code:** it lives in `lib/*.ps1`, loaded by `lib/HandoverKit.psm1`. Put literals in `lib/KitConstants.ps1`, and give scripts layout names through `Get-KitLayout`.

## Rules

- **Nothing is deleted outside `-OutputPath`,** except the scripts' own `%TEMP%\codelab-handover-*` folders.
- **A non-empty `-OutputPath` is refused unless `-Force` is given.** Even with `-Force`, the folder must hold a kit `manifest.json` and nothing but kit entries, and only those entries are removed. A failed run removes the entries it wrote, so no half kit is left.
- **Restores never run in the caller's tree.** They use a temp copy built from git's file list (tracked plus untracked files that are not ignored), so the caller's `obj\` and `node_modules\` are untouched.
- **The kit has no online source.** Its `nuget.config` has `<clear/>` and the folder source only; the offline test also blocks HTTP through an unroutable proxy.
- **Windows PowerShell 5.1 compatible:**
  - no `&&`, `?:` or `??`;
  - no `$PSScriptRoot` in parameter defaults;
  - text is written as UTF-8 without a BOM;
  - lock files are read as dictionaries, because 5.1 `ConvertFrom-Json` rejects the lock file's empty `""` key.

## Gotchas

- **The kit holds dependencies and Codelab package sources, not the application source.** The client builds the delivered source tree, and `Test-HandoverKit.ps1` builds the manifest's `sourceRoot` (or `-SourceRoot`).
- **The in-process test is a simulation.** It blocks proxies and starts from an empty package folder, but it does not disable the network adapter. The real proof, a VM with the adapter disabled, is an owner step (ADR-008).
- **The kit restores only what the listed projects need.** A `.slnx` covers all its projects; otherwise list every project the client builds, test projects included.
- **`Microsoft.AspNetCore.App` and other shared frameworks come from the installed SDK**, not from `nuget\`. Install the SDK version in `PREREQUISITES.md`.
- **CI step 14 runs the offline simulation on the exit-criterion targets** (`Package.Sample.Tests` and `ng-security-consumer`, at the versions step 13 tested). The unit tests run in step 10; the opt-in `end-to-end.test.mjs` (`CODELAB_HANDOVER_E2E=1`) adds the negative proof that a kit missing a package fails offline. The phase 1 exit criterion "network-disabled restore and build" stays open until the owner's VM run.
