# tools/fork

## Purpose

The fork model (ADR-002, ADR-003, ADR-004). A project forks `codelab-starter`, runs `init.ps1` once, and later takes
starter fixes with `tools/upstream-merge.ps1`, which replays the fork's rename on the incoming starter diff. This
folder holds the engine behind four PowerShell entry points, the manifest schema and the tests:

| Entry point | What it does |
| --- | --- |
| `init.ps1` (repo root) | The full init (ADR-003): asks for names, prefix, logo, colour, login modes and optional modules (or reads them from `-ConfigFile`), then runs rename, module/package removal, local keys, local database and the manifest/`Auth:Modes` step, each skipped when already done (`lib/init-steps.mjs`). |
| `init.ps1 -Minimal` (repo root) | Turns a fresh starter clone into a fork in one recorded commit: removes modules and `packages/`, switches to feed mode, applies the rename map, sets the product prefix, hashes the source-owned files and writes `codelab.starter.json`. |
| `tools/remove-module.ps1 -Name <Name>` | Removes one module at file level and records it in `removedModules`: its folders, its `.slnx` projects and host `ProjectReference`s, every line or block of a shared file that carries its module marker, and its `<camelName>.*` translation keys (see Module markers below). |
| `tools/upstream-merge.ps1 -Upstream <remote or path>` | Merges starter changes since `starterBase`: rename replay, keep-deletion for removed areas, `git apply -3`, conflict report, hashes, build. |
| `tools/fork/Test-ForkModel.ps1 -RemoveModule <Name>` | The real-repo fork-model test: snapshot, scaffold, init, upstream change, one expected conflict, merge, feed-mode build, feed-origin checks. |
| `tools/ci/module-deletability.ps1 -Name <Name>` | The deletability proof: removes the module from a temp copy, then builds and tests it. Uses `fork.mjs add-probe` (with `-DummyModule`) and `fork.mjs verify-removed`. |

| File | Role |
| --- | --- |
| `fork.mjs` | The one Node CLI the `.ps1` files call (`init`, `remove-module`, `upstream-merge`, `fork-model`, `add-probe`, `verify-removed`). |
| `codelab.starter.schema.json` | JSON schema of `codelab.starter.json` (the manifest; the starter ships the template with `starterBase: null`). |
| `lib/constants.mjs` | Every name, path, limit and default. Nothing else in this folder holds one. |
| `lib/rename.mjs` | The rename engine: case variants, longest first, identifier boundaries, protected names. |
| `lib/diff.mjs`, `lib/merge-plan.mjs` | Pure diff parsing/rewriting and the apply/drop/conflict decision per file. |
| `lib/module-markers.mjs` | Pure marker stripping (`stripModuleMarkers`) and translation-prefix removal (`removeI18nPrefix`). |
| `lib/deletability.mjs` | The deletability probe (`add-probe`, temp copies only) and the remnant check (`verify-removed`). |
| `lib/upstream-merge.mjs`, `lib/init.mjs`, `lib/remove-module.mjs`, `lib/fork-model.mjs` | The flows (git and file system). |
| `lib/ForkCommon.ps1` | Shared by the `.ps1` entry points: pinned Node, list splitting, calling `fork.mjs`. |
| `tests/` | `node --test` unit tests of every pure module, plus end-to-end tests on a synthetic starter in `%TEMP%`. |

## Use

```powershell
# In a fresh fork clone (npm ci first: steps 1 and 2 are BLOCKED without node_modules; step 2 reinstalls npm after
# packages/ goes): interactive, or non-interactive for CI
powershell -NoProfile -ExecutionPolicy Bypass -File init.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File init.ps1 -ConfigFile C:ciinit.json -WhatIf   # plan only
powershell -NoProfile -ExecutionPolicy Bypass -File init.ps1 -ConfigFile C:ciinit.json -SkipDatabase
# Config file (a relative logoPath is resolved against the file's folder):
#   { "prefix": "ACME", "nameEn": "Acme Portal", "nameAr": "...", "primaryColor": "#0a5c8f", "logoPath": null,
#     "loginModes": ["basic", "sso"], "optionalModules": { "attachments": true, "portal": true, "sms": false, "ops": true },
#     "database": { "connectionString": null, "skip": true } }
# The scripted Phase 2 init (fork-model tests)
powershell -NoProfile -ExecutionPolicy Bypass -File init.ps1 -Minimal -ProductPrefix ACME -RemoveModule Demo -FeedDir E:\codelab-starter\feed

# Later, in the fork: take starter changes
git remote add upstream <starter url or path>
powershell -NoProfile -ExecutionPolicy Bypass -File tools\upstream-merge.ps1 -Upstream upstream
#   exit 1 = conflicts: resolve, git add, then
powershell -NoProfile -ExecutionPolicy Bypass -File tools\upstream-merge.ps1 -Continue

# Remove one more module from a fork
powershell -NoProfile -ExecutionPolicy Bypass -File tools\remove-module.ps1 -Name Demo

# The real-repo fork-model test (needs the scaffolder, backend and frontend; wave 4 wires it into CI)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\fork\Test-ForkModel.ps1 -RemoveModule Demo

# Prove a module is deletable (temp copy; build + tests); before the sample exists, with a scaffolded dummy
powershell -NoProfile -ExecutionPolicy Bypass -File tools\ci\module-deletability.ps1 -Name ServiceRequests
powershell -NoProfile -ExecutionPolicy Bypass -File tools\ci\module-deletability.ps1 -Name DeletionProbe -DummyModule

# This folder's tests (spawn Windows PowerShell 5.1)
node --test "tools/fork/tests/**/*.test.mjs"
```

CLI contracts (all scripts are Windows PowerShell 5.1; list parameters accept `A,B`):

| Script | Parameters | Exit codes |
| --- | --- | --- |
| `init.ps1` (full) | `-ConfigFile <json>` (else interactive), `-WhatIf`, `-SkipDatabase`, `-SkipSecrets`, `-SkipNpmLock`, `-FeedDir <dir>` | 0 done (or every step already done), 1 refused (invalid config, a blocked step, dirty tree), 2 usage/internal |
| `init.ps1 -Minimal` | `-Minimal`, `-ProductPrefix <P>`, `-RemoveModule <Name[]>`, `-FeedDir <dir>` (default `<repo>\feed`), `-RenameMap <json file>` (default identity), `-SkipSecrets`, `-SkipNpmLock`, `-AllowMissingTargets` | 0 done, 1 refused (nothing changed), 2 usage/internal |
| `tools/remove-module.ps1` | `-Name <Name>` | 0 removed or already removed, 1 refused, 2 usage/internal |
| `tools/ci/module-deletability.ps1` | `-Name <Name>`, `-WorkDir <dir>` (default `%TEMP%\codelab-module-deletability-<Name>`), `-SourceRoot`, `-Revision <rev>` + `-Overlay <paths>`, `-DummyModule`, `-NpmCi`, `-SkipBackendTests`, `-SkipFrontend`, `-KeepCopy`, `-PlanOnly` | 0 passed, 1 a check failed (step named), 2 usage, guard refusal or setup failure |
| `tools/upstream-merge.ps1` | `-Upstream <remote or path>` and `-Branch <name>` (default `main`), or `-Continue`; `-BuildCommand <PowerShell text>`, `-SkipBuild`, `-ReportPath <json>` | 0 merged clean or up to date, 1 conflicts left (or the build failed), 2 usage/internal |
| `tools/fork/Test-ForkModel.ps1` | `-RemoveModule <Name>`, `-StarterRoot`, `-FeedDir`, `-EditFile <5 files>`, `-ConflictFile`, `-AddFile`, `-KeepScratch`, `-SkipBuild`, `-SkipSecrets`, `-SkipNpmLock`, `-AllowMissingTargets` | 0 passed, 1 assertion failed, 2 usage/setup |

The `-ReportPath` JSON: `{ status: clean|conflicts|up-to-date|build-failed, previousBase, upstreamCommit, applied: [path], dropped: [{ path, upstreamPath, reason }], conflicts: [{ path, kind }] }`. Drop reasons: `removed-area`, `regenerated-output`, `already-deleted-in-fork`, `removed-module-lines-only`. Conflict kinds: `content`, `modified-upstream-deleted-in-fork`, `added-upstream-exists-in-fork`.

### Module markers

A module that registers itself in a shared file marks every such line, so remove-module can delete it without
knowing the file:

```ts
import { SERVICE_REQUESTS_STAFF_ROUTES } from './features/service-requests/service-requests.routes'; // codelab:module ServiceRequests
// codelab:module-begin ServiceRequests
{ path: 'staff/service-requests', loadChildren: () => import('./features/service-requests/staff.routes') },
// codelab:module-end ServiceRequests
```

```xml
<!-- codelab:module-begin ServiceRequests -->
<ItemGroup>...</ItemGroup>
<!-- codelab:module-end ServiceRequests -->
```

- A marker counts only after a comment opener (`//` or `<!--`) and only for the exact name (`ServiceRequests` never
  matches `ServiceRequestsArchive`). Begin/end lines are removed with the block.
- Scanned: `.ts`, `.html` and `.csproj` files under `frontend/starter-web/src/` and `backend/src/`
  (`MARKER_SCAN_ROOTS`, `MARKER_SCAN_EXTENSIONS`), outside `node_modules`, `bin`, `obj`, `dist`, `.angular`.
- Translations: the module owns the top-level key `<camelName>` (`serviceRequests`) and any flat key starting with
  `<camelName>.` in `ar.json` and `en.json` (`I18N_FILES`). Key order, indent, line endings and the final line
  break are kept. Shared keys (`records.*`, `permissions.*`) are not the module's and stay.
- An end without a begin, a begin without an end or a nested begin of the same module is refused before anything
  changes.

## Extend

- **A new name, path or default:** add it to `lib/constants.mjs` and use it from there.
- **A new manifest field:** change `codelab.starter.schema.json`, `MANIFEST_FIELDS` and `validateManifest` in `lib/manifest.mjs` together; `tests/manifest.test.mjs` runs both validators over the same samples and fails when they disagree. If the field name holds a renameable token, add it to `RENAME_PROTECTED_NAMES`.
- **A new registration file:** mark the module's lines (above). A file outside the scan roots or extensions needs its
  root or extension added to `MARKER_SCAN_ROOTS` / `MARKER_SCAN_EXTENSIONS` and a case in `tests/module-markers.test.mjs`.
- **A new file that lists modules line by line:** add it to `MODULE_LIST_FILES`, so upstream-merge drops removed modules' lines from its patches.
- **A new generator output:** add it to `RULES_OUTPUTS` or `MODULE_RULES_OUTPUTS` (upstream-merge regenerates instead of merging), or to `HASH_EXCLUDED_PREFIXES` when it must not be hashed.
- **A new init step:** put the logic in a pure `lib/` function with a unit test, call it from `runInit` (validate first, change after), and cover it in `tests/upstream-merge.e2e.test.mjs`.

## Rules

- **Rename safety (ADR-003).** Starter code must derive every name from the `Starter`/`starter`/`STARTER` tokens a rename map can find, never from parts built at run time. This folder follows the rule itself: init renames a fork's copy of `lib/constants.mjs` with everything else, so a fork's tools always name the fork's own paths. Manifest names (`codelab.starter`, `starterBase`, `starterVersion`) are rename-protected by identity entries init adds to `renameMap`.
- Validate before changing: every refusal leaves the repo as it was. A missing optional target is skipped only with `-AllowMissingTargets`, never silently.
- Never run `init.ps1` or `upstream-merge.ps1` against the starter repo itself; they are for forks. `Test-ForkModel.ps1` only reads the starter (it works in `%TEMP%` and never writes the repo's `feed\`).
- The scripts never commit a merge and never push. init makes exactly one commit.
- No npm dependency at run time: a fresh fork clone runs these tools before `npm ci` (only the rules compiler needs `node_modules`).
- No waits or sleeps; no magic values outside `lib/constants.mjs`; unit tests for all logic.

## Gotchas

- upstream-merge does not yet drop a removed module's marker lines from upstream patches (only its solution and host
  lines, `MODULE_LIST_FILES`): an upstream change next to a removed module's marked lines in `app.routes.ts` can
  surface as a conflict to resolve by hand.
- Generated files that list a removed module's permissions or endpoints (`docs/generated/`,
  `permission-keys.generated.ts`, `openapi.ts`) are not regenerated by remove-module; rerun DocsGen and `api:gen`.
- Windows PowerShell 5.1 passes `-RemoveModule A,B` to a `-File` script as one string; `Split-ForkList` splits it. It also mangles quotes in native-command arguments, so `-BuildCommand` travels to Node in `CODELAB_FORK_BUILD_COMMAND` and runs through `-EncodedCommand`.
- `git apply -3` falls back to a three-way merge only when the pre-image blob exists. For a renamed fork the merge writes the renamed pre-image (and post-image) blobs into the fork's object database first, so a fork edit next to an upstream edit merges instead of failing.
- The solution and the host project lose removed modules' lines, so their upstream hunks are regenerated from the transformed blobs (`git diff <blob> <blob>`) instead of being patched line by line.
- The rules outputs (`rules/*.compiled.json`, `rules/codelab.globalconfig`, per-module `CLAUDE.md`, `BannedSymbols.generated.txt`, `.editorconfig`) are dropped from the upstream diff and regenerated after a clean merge or `-Continue`.
- The fork's `feed\` contents are gitignored, so a fresh clone of a fork has an empty feed until a hosted feed replaces the folder (ADR-002); init refuses when the copied packages would be tracked.
- In the default build step, `tools\build.ps1 -Target CI` of a fork still runs the starter's package steps (11-14), which need `packages/`; until wave 4 makes build.ps1 fork-aware, pass `-BuildCommand` (for example `dotnet build codelab-starter.slnx -p:UseCodelabPackageSource=false`).
- Text is handled as UTF-8; a non-UTF-8 text file in an upstream diff would not survive the rewrite (git apply then fails and nothing is applied).
