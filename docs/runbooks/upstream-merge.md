# Runbook: take starter changes into a fork (upstream merge)

**Who:** the fork's tech lead. **When:** after a starter release, or when a starter fix is needed. **Takes:** 15 to 60
minutes, plus conflict time. **Status:** final (phase 10); proven by `tools/fork/tests/upstream-merge-exit.e2e.test.mjs`
(a core-only fork merges a starter commit cleanly).

A fork is a copy of `codelab-starter` that ran `init.ps1` once: it may have renamed `Starter` to its own product
name, removed modules and `packages/`, and switched to the Codelab package feed. `tools/upstream-merge.ps1` replays
that rename on the starter's changes since the fork's `starterBase` (recorded in `codelab.starter.json`), so a
starter fix applies to renamed files. The engine and its contracts are in [tools/fork/README.md](../../tools/fork/README.md);
the decisions behind it are ADR-002, ADR-003 and ADR-004 in [docs/adr/](../adr/README.md).

## 1. Before you start

1. Work on a branch of the fork, never on `main` directly: `git switch -c chore/upstream-merge-<date>`.
2. The work tree is clean (`git status --porcelain` prints nothing): the script refuses otherwise.
3. The fork builds green on that branch before the merge, so any failure afterwards comes from the merge.
4. The starter is reachable as a remote (once per clone):

   ```powershell
   git remote add upstream <starter repository url or path>
   ```

5. Read what changed: the starter's `packages/CHANGELOG.md` and the ADRs added since `starterBase`.
6. See which files the fork changed since its last init or merge (that is where conflicts will come from):
   `codelab.starter.json` `hashes` holds each file's hash (line endings normalized) at that time; list the files
   whose hash differs now:

   ```powershell
   node --input-type=module -e "import { computeHashes } from './tools/fork/lib/hashing.mjs'; import { existsSync, readFileSync } from 'node:fs'; import { execSync } from 'node:child_process'; const recorded = JSON.parse(readFileSync('codelab.starter.json', 'utf8')).hashes; const now = computeHashes('.', execSync('git ls-files -z').toString().split('\0').filter((f) => f && existsSync(f))); for (const f of new Set([...Object.keys(recorded), ...Object.keys(now)])) if (recorded[f] !== now[f]) console.log(f);"
   ```

   After the merge the script rewrites `hashes` for the new tree, so the same command run on the merged branch
   prints nothing until the fork changes a file again.

## 2. Merge

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\upstream-merge.ps1 -Upstream upstream -Branch main -ReportPath $env:TEMP\upstream-merge.json
```

What it does, in order: fetches `upstream/main`, diffs it against `starterBase`, rewrites every path and identifier
through the fork's rename map, drops what the fork removed, applies the rest with `git apply -3 --index`, moves
`starterBase` to the fetched commit and, on a clean merge, regenerates the rules outputs, recomputes the hashes and
runs the build (`tools\build.ps1 -Target CI`, or `-BuildCommand <text>`; `-SkipBuild` skips it). Changes are staged,
**never committed or pushed**.

| Exit code | Meaning | Next |
| --- | --- | --- |
| 0 | merged clean (or already up to date) and the build passed | review the staged diff (section 4), commit |
| 1 | conflicts are left for you, or the build failed | section 3 |
| 2 | usage or internal error; nothing was applied | read the message (wrong remote, dirty tree, not a fork) |

The report (`-ReportPath`) lists `applied`, `dropped` (with a reason) and `conflicts` (with a kind). Keep it with the
pull request.

## 3. Conflict policy

Resolve each conflict in the files git marks, `git add` them, then finish with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\upstream-merge.ps1 -Continue
```

`-Continue` checks no conflict is left, regenerates, rehashes and builds.

| Conflict kind | Rule |
| --- | --- |
| `content` (both changed the same lines) | **Starter wins for platform code** (`Codelab.*` and `@codelab/*` consumers, `rules/`, `tools/`, `deploy/`, `.claude/`, the kernel and shared UI): take the starter side, then re-apply the fork's change only if it is still needed, and open a starter issue for it. **Fork wins for business code** (its own modules, screens, texts, branding): keep the fork side and take the starter's fix by hand. |
| `modified-upstream-deleted-in-fork` | The fork deleted the file on purpose: keep it deleted. If the starter change is a security fix, check whether the fork has a copy of that code elsewhere and fix it there. |
| `added-upstream-exists-in-fork` | Both added a file with the same path: keep the fork's file, then compare and take any starter part it needs. Rename the fork's file when the two are different things. |

Per module:

| Module | On conflict |
| --- | --- |
| Identity, Access (required) and the kernel (`Starter.Api`, shared UI, `@codelab/ng-dga` consumers) | Starter wins; re-apply fork changes on top. |
| Optional modules the fork kept (attachments, portal, sms, ops) | Starter wins for their code; fork wins for their texts, seeds and settings. |
| Optional modules the fork removed | Never a conflict: their files, marked lines (`codelab:module <Name>`) and translation keys are dropped. Do not re-add them. |
| The fork's own business modules | Fork wins; take starter fixes by hand. |
| Migrations | Never edit a merged starter migration; add a new fork migration if the fork's model differs. |

What is never merged by hand:

- **Dropped areas** (`removed-area`, `already-deleted-in-fork`, `removed-module-lines-only`): a module or `packages/`
  the fork removed stays removed; the report lists what was skipped. Do not re-add it.
- **The manifest** (`fork-manifest`): `codelab.starter.json` is the fork's; the script only moves `starterBase`, takes
  the starter's `starterVersion` and recomputes `hashes`.
- **Generated outputs** (`regenerated-output`): `rules/*.compiled.json`, module `CLAUDE.md`, `BannedSymbols.generated.txt`,
  `.editorconfig`, `docs/generated/`. Never resolve them by hand; they are regenerated (rerun DocsGen and
  `api:gen` for `docs/generated/` and the generated API client when the report lists them).
- **Secrets and server settings:** a merge never brings keys or environment values; `appsettings.Development.local.json`
  stays local, and `tools\devseed\New-LocalSecrets.ps1` adds keys a new starter feature needs.

## 4. Review before committing

1. `git diff --cached --stat`: every change is one the starter shipped (compare with the report's `applied`).
2. No starter names leaked into a renamed fork: search the staged diff for the starter's product token.
3. Migrations: a new starter migration in a module the fork kept is expected; a migration in a removed module is not
   (it should have been dropped).
4. Run the fork's own checks (build, tests, the frontend build) if the script's build step was skipped.
5. Commit with a message naming the starter commit (`starterBase` in `codelab.starter.json`), then open a pull request
   with the report attached.

## 5. Package bumps

A fork does not build the Codelab packages; it consumes them from its feed (ADR-002). A starter release that bumps a
package (`packages/versions.json` and `packages/CHANGELOG.md` upstream) is a separate step from the source merge,
because the fork's `packages/` folder is a dropped area:

1. Read the CHANGELOG entries between the fork's pinned versions and the new ones. A major version means a breaking
   change with migration notes; read them before bumping.
2. Make the new versions available in the fork's feed (the hosted feed, or the `feed\nuget` and `feed\npm` folders
   of the handover kit for an offline client).
3. Bump the pins: the `Codelab.*` `PackageVersion` items in `Directory.Packages.props` (the item group init labelled
   for the feed), `CodelabBuildSdkVersion` in `Directory.Build.props` when the build SDK changed, and the `@codelab/*`
   entries of `package.json` and `frontend/starter-web/package.json` (renamed paths in a renamed fork), then
   `npm install` to refresh the lock file.
4. Build and test. Merge the source first (sections 2 to 4), then bump the packages in a second commit, so a
   failure points at one of the two.

**Package update policy (manual).** No update bot: Renovate and Dependabot are not approved, so every bump is a person
following this section.

- **Codelab packages:** take every starter release within one month; a security release (CHANGELOG marks it) within
  5 working days, without waiting for the source merge.
- **Third-party packages** (NuGet in `Directory.Packages.props`, npm in `package.json`): once a month, list what is
  outdated (`dotnet list package --outdated --vulnerable`, `npm outdated`, `npm audit`). Patch and minor bumps go in one
  pull request; each major bump goes in its own pull request with its release notes read. A vulnerable package is
  bumped as soon as a fixed version exists.
- Prefer the versions the starter pins: a fork that bumps a shared package ahead of the starter gets a
  `Directory.Packages.props` conflict on the next merge (fork wins only when the fork's version is higher).
- Versions are pinned centrally (`Directory.Packages.props`, exact npm versions); never a `Version` on a
  `PackageReference`, never a range.

## 6. Rollback

Nothing is committed until you commit. To abandon a merge in progress: `git restore --staged --worktree .` on the
merge branch (only on that branch, and only for the merge's own changes), or delete the branch and start again.
After a commit, revert the merge commit; `starterBase` returns with it.

## Gotchas

- Run the script in the fork, never in the starter repository itself.
- A removed module's marked lines (`codelab:module <Name>`, begin/end blocks) and translation keys are filtered out of
  the upstream change before it applies. A marker the starter left unpaired is not filtered and shows up as a
  `content` conflict; resolve it by keeping the module's lines out.
- The default build step runs `tools\build.ps1 -Target CI`, which still has starter-only package steps; until the
  build is fork-aware, pass `-BuildCommand` with the fork's own build (see [tools/fork/README.md](../../tools/fork/README.md)).
- A non-UTF-8 text file in the upstream diff makes `git apply` fail and nothing is applied; convert it in the starter.
