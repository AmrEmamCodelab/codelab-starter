# ADR-005: Node pin (24 now; 24.15 or later before phase 2)

- **Status:** Accepted
- **Date:** 2026-11-04
- **Deciders:** Amr Emam (platform owner)

## Context

The plan asks for the newest Node Active LTS that Angular 22 supports (D8). Angular 22.2.x declares `engines.node` as `^24.15.0 || >=26`. Node 26 is not yet the Active LTS line, so Node 24 is the newest compatible LTS. The build machine has Node 24.14.1, which is below Angular's 24.15.0 floor. Phase 0 has no Angular code, and its Node tooling (rules engine, hooks, scrub and secret scans, CI checks) runs on Node 24.14.1.

## Decision

1. `.nvmrc` pins **`24`**, and the root `package.json` declares `"engines": { "node": ">=24" }`.
2. **Node 24.15.0 or later is required before phase 2** starts the Angular 22 frontend. At that point `.nvmrc` moves to that minimum or newer, and the frontend `package.json` engines mirror Angular's range.
3. Move to Node 26 once it is the Active LTS and Angular supports it, and record that in a superseding ADR.
4. Node tooling in `tools/` and `.claude/hooks/` stays dependency-light (`yaml`, `ajv`, `ajv-formats`). It uses ES modules (`.mjs`) and the built-in `node --test` runner.

## Consequences

- Phase 0 runs on the installed Node. Every developer machine and CI agent must be upgraded to 24.15 or later before phase 2.
- `node --test <folder>` does not work on Node 24, so `tools/build.ps1` passes a `**/*.test.mjs` glob instead.
