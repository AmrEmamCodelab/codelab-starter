# ADR-007: Repository host and CI (local now, GitHub and Actions later)

- **Status:** Accepted
- **Date:** 2026-11-04
- **Deciders:** Amr Emam (platform owner)

## Context

The phase 0 entry gate in the plan asks for a repository host and a CI system. No hosted repository is available yet, but phase 0 still needs a working, authoritative CI. Moving to a host later must not require rewriting CI logic.

## Decision

1. **Now:** a local git repository on branch `main`. CI is `tools\build.ps1 -Target CI`, run locally, and that script holds **all** CI logic. Packages go to the local folder feed (ADR-002).
2. **Later: GitHub, with GitHub Actions and GitHub Packages.** The workflow YAML only calls `tools/build.ps1`, so the move is **configuration only**: the workflow file, the feed URLs in `nuget.config` and `.npmrc`, and branch protection.
3. **Already in place for the move:**
   - `.github/CODEOWNERS` assigns `rules/`, `packages/`, `.claude/`, `**/module.rules.yaml` and `deploy/` to the platform owner. `@platform-owner` is a placeholder for Amr Emam's handle.
   - `.github/PULL_REQUEST_TEMPLATE.md`.
4. **Branch protection on `main`, once hosted:** a pull request, green CI and code-owner review. There is no backup reviewer; the owner merges protected changes as admin once CI is green.
5. **Runner pool, once hosted:** Linux runners for the SQL Server container (and the IdP/LDAP containers if approved), and Windows runners for IIS, LocalDB and the Windows Server 2025 kit tests.
6. **The docs override is host-neutral:** a `Docs-Not-Needed: <reason>` trailer in the commit message or PR description, read by `build.ps1`, rather than a host label.

## Consequences

- Until a host exists, protection is procedural: run `tools\build.ps1 -Target CI` before every commit to `main`.
- CI behaves identically locally and in the hosted pipeline.
- Having a single owner review protected changes is a bus-factor risk. It is accepted for now and revisited when the team grows.
