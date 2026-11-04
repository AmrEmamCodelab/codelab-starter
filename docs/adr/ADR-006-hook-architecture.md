# ADR-006: Hook architecture (CI is authoritative, hooks are the early warning)

- **Status:** Accepted
- **Date:** 2026-11-04
- **Deciders:** Amr Emam (platform owner)

## Context

Projects are built mostly by Claude Code, and in MODHS the prose-only safety rules were skipped in practice. Claude Code hooks can refuse an edit before it lands. However, hooks run only inside Claude Code, can be disabled locally, and do not see edits made by people or other tools. The rules also need to live in one place that both humans and machines can read.

## Decision

1. **There is a single source of rules:** `rules/base.rules.yaml`, plus one `module.rules.yaml` per module.
   - The fields are `mayDependOn`, `mustNotDependOn`, `required`, `forbidden` (pattern plus fix message), `recipes` and `owners`, with inheritance from base.
   - The rules engine compiles them to `rules/rules.compiled.json`, which is drift-checked in CI. Neither hooks nor CI parse YAML at runtime.
   - The engine emits data only: nested `CLAUDE.md` files, per-project `BannedSymbols.generated.txt` and `.editorconfig` sections, and ESLint boundary data.
2. **Hooks are Node scripts** (`.claude/hooks/*.mjs`) that read `rules.compiled.json`:
   - PreToolUse runs on `Edit|Write|Bash|PowerShell`, in a worker with a time budget; a rule that runs out of time denies the call instead of failing open.
     - `Edit` is evaluated on the **whole resulting file**: the old-to-new change is applied to a temporary copy first.
     - `Write` is evaluated on the new content.
     - Shell commands that write into source paths are refused (redirects, heredocs, in-place editors, copy/move/remove, downloads, `git checkout -- <path>`, PowerShell cmdlets and aliases, .NET file APIs, nested `bash -c`/`cmd /c`/`eval`/`iex`/`-EncodedCommand`). A write whose path cannot be resolved fails closed: the user is asked.
     - A violation is refused with the rule id and the fix message.
   - Module docs are injected through whichever mechanism the phase 0 spike proves works, including for a new file in a new module folder.
   - Stop runs the docs gate (with `--base`) and the rule checks on everything changed since the merge base with main, committed or not.
   - Deny rules block `git push --force` and reading `*.pfx`/`*.key`. A platform-owner flag gates edits to the CODEOWNERS paths and every guardrail switch (build script, scrub lists, repo-wide MSBuild and SDK settings) and to the `exemptions` block of a `module.rules.yaml`.
3. **CI is the authoritative enforcement.** `tools/build.ps1 -Target CI` runs the same checks no matter who or what made the change: scrub sweep, secret scan, rules drift, tests, a `-warnaserror` build with banned APIs, the docs gate against the base ref, conflict markers, and the **content check** (`tools/rules-engine/check-content.mjs`), which evaluates every forbidden/required rule over every tracked file in absolute state with the same evaluator the hooks use. Path-owner review (CODEOWNERS) covers `rules/`, `packages/`, `.claude/`, `**/module.rules.yaml`, `deploy/`, `.github/`, `docs/adr/`, `provenance/`, `tools/extract/`, `tools/build.ps1` and the repo-wide build settings. Until a host enforces CODEOWNERS, `build.ps1` lists the protected paths a branch changes.

## Consequences

- Bypassing a hook only delays the failure until CI; it never lets a violation merge.
- Every rule must be machine-checkable in CI first. A rule enforced only by a hook is not allowed.
- Hooks need their own fixture tests (`.claude/hooks/tests`) so they stay correct as the rules change.
