# ADR-001: Fresh history, extraction manifest and durable harvest copy

- **Status:** Accepted
- **Date:** 2026-11-04
- **Deciders:** Amr Emam (platform owner)

## Context

The starter is harvested from MODHS. Its history and working tree belong to a different product and environment (deployment configuration, local tooling state and project-specific settings), and it contains integrations that are out of scope for v1 (Nafath, Yakeen/NIC). None of that may travel with the harvested code, so only reviewed files cross over and no history does. MODHS must not be touched (D6). The harvest must be repeatable and auditable months later, so it cannot read from a moving branch, a developer's working copy or `%TEMP%`.

## Decision

1. **Fresh history.** `codelab-starter` is a new repository with no MODHS history. Nothing is ever merged or cherry-picked from MODHS.
2. **Durable read-only harvest copy.** The single source for harvesting is `E:\codelab-harvest\modhs-a6a4b134`. It was made with read-only git commands only: `git -C E:\MODHS -c core.autocrlf=false archive a6a4b134` (branch `dev_merge_sso`, 2026-11-03), extracted and marked read-only. A file-hash manifest is verified against `git ls-tree -r a6a4b134` using `git hash-object --no-filters`. The tree hash is `5f0ef9e7e76a7fdb5e828108f1c86ab67e2f65d4`, recorded in `provenance/SOURCE.md`. Builders never write to `E:\MODHS` or to the harvest copy.
3. **Allowlist extraction.** Only `tools/extract/extract.ps1` brings harvested files into the repo. It copies allowlisted paths (`tools/extract/allowlist.manifest.yaml`) into a staging folder, scans them, and writes a per-file origin map into `provenance/`.
4. **Hard-denied paths:** Claude memory, `.claude/handoffs`, `prep-deploy/REFERENCE.md`, `deploy/config/*.json`, the `check-env-*` scripts, `services/nafath-service/**`, `services/nic-simulator/**`, any file whose name matches `*Nafath*`, `*Yakeen*` or `*Nic*`, and any `@dev-dga` tarball.
5. **Scans run before anything is committed, and on every CI run:**
   - `tools/extract/secret-scan.mjs` applies regex rules for keys, tokens, connection strings and private keys, plus a SHA-256 digest denylist of known MODHS key material and fixture keys.
   - gitleaks runs in addition, **but only when `gitleaks.exe` is available** (on PATH or at `tools\bin\gitleaks.exe`). It is not installed on the build machine today and could not be downloaded, so our own scanner is the guaranteed gate.
   - `tools/extract/scrub-sweep.mjs` checks MODHS terms (`tools/extract/modhs-terms.txt`), brands, hostnames/IPs, national-ID-like numbers and the external DGA package rule. It covers all files, including `rules/`, `.claude/`, `docs/` and every `CLAUDE.md`. The only exceptions are `provenance/` and `docs/adr/`: these are records that must name their source. Planning inputs (the build plan and the decision log) stay outside the repository, because every fork inherits what is in it; both excepted folders are platform-owned (CODEOWNERS and the hook's platform-owner guard).
6. **Starter names only.** Everything outside the exception paths uses starter names (`Starter.*`, `Codelab.*`, `@codelab/*`).

## Consequences

- No secret can enter through history, and every harvested file has a recorded origin.
- The harvest is pinned. Newer MODHS fixes reach the starter only through a deliberate new harvest, with a new copy and a new ADR.
- The scrub sweep blocks accidental reuse of MODHS domain language, so new docs must use starter vocabulary.
- When gitleaks becomes available, the custom secret scanner stays; both run.
