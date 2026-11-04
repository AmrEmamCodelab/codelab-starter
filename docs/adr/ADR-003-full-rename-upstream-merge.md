# ADR-003: Fork naming is a full rename; upstream merges replay the rename

- **Status:** Accepted. Decided 2026-11-04, ahead of the planned decision point at the end of phase 2.
- **Date:** 2026-11-04
- **Deciders:** Amr Emam (platform owner)

## Context

Every project forks the starter and must look like its own product. That means its own solution, project and namespace names, database, IIS sites, npm app, display names (EN/AR), branding and protocol prefix. Projects must still take starter source fixes through a merge (D3). Two options were considered:
- **Keep neutral names in forks** (`Starter.*` forever; only display names change). Merges are trivial, but every client codebase is named after a template.
- **Fully rename each fork.** The code is product-native, but a plain `git merge upstream/main` would conflict on every renamed path and identifier.

## Decision

**Full rename.**
1. The starter is written with neutral `Starter.*` folders, file names, C# namespaces, assembly names and TypeScript paths.
2. `init.ps1` renames all of them in the fork, in one recorded commit. For example, `Starter.Workflow` becomes `MohPermits.Workflow`. The same commit also renames the database, IIS names, npm app name, display names (EN/AR), branding, the protocol prefix (`{Prefix}-SIG-v1`, `X-{Prefix}-...`) and the numbering prefix.
3. The complete old-to-new map is saved as `renameMap` in `codelab.starter.json`. The same file records `starterBase` (the starter commit the fork is based on), `removedModules`, and normalized content hashes of source-owned files.
4. `tools/upstream-merge.ps1` brings in upstream changes in these steps:
   - It fetches `upstream/main` and takes the starter diff since `starterBase`.
   - It **rewrites every path, namespace and identifier in that diff through `renameMap`**.
   - It applies the result with a 3-way apply (`git apply -3`), so conflicts appear only where the project changed the same lines.
   - It resolves modify/delete conflicts under `removedModules` and `packages/**` by keeping the deletion, and drops upstream-added files under those paths.
   - It updates `starterBase`, recomputes the hashes and runs `tools/build.ps1`.
5. Packages are unaffected by the rename; they flow by version bump (ADR-002).

## Consequences

- Forks are product-native from day one.
- The merge tool is a real product. It costs about 2 extra person-weeks across phases 2 and 10, and it needs an upstream-merge runbook.
- From phase 2 a fork-model CI test guards it. The test inits a scratch fork, then on the starter side changes 5 files, adds 1 and touches a removed module. It then merges, and the fork must build green.
- Starter code must keep identifiers rename-safe. Names derive from `Starter`/`starter` tokens that the rename map can find, never from hand-built strings.
- No MODHS identifier may appear in starter code (the ADR-001 scrub sweep), so a rename never has to undo one.
