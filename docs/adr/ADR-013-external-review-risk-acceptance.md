# ADR-013: Ship v1.0 without the external security review (risk acceptance)

- **Status:** Proposed (2026-11-07), pending platform-owner confirmation; not accepted until the owner decision is recorded in the phase log
- **Date:** 2026-11-07
- **Deciders:** Amr Emam (platform owner)

## Context

The build plan made an external security review of the `Codelab.*` / `@codelab/*` security core (A6) an exit criterion for v1.0, if A6 was approved. A6 was not approved: no external review was booked or run.

The core contains bespoke protocol code: canonical request framing, request and response signatures, the PublicId ring, sealed tickets and the ES256 service-to-service assertions. Standard primitives come from the platform (.NET and WebCrypto); the framing and the protocol around them are ours. Bespoke protocol code that nobody outside the team has reviewed is the main residual risk of v1.0.

## Decision

v1.0.0 ships without an external review. The risk is accepted, and limited by what already runs on every change:

1. **Golden vectors.** `packages/vectors` (TEST-ONLY fixture keys, prefixes `CODELABTEST` and `ALTPFX`) pin the bytes of every signature, PublicId and session-key format. The C# and TypeScript implementations must both match them (`tools/vectors/verify-vectors.mjs`, the package tests and the feed-only sample in CI step 13), so a change to the framing fails the build.
2. **Fail-closed defaults and boot guards.** Published fixture keys, undefined enum modes, a direct back channel without egress and unsafe CORS refuse to start; fixture keys are refused by `tools/devseed/Test-NoFixtureKeys`.
3. **Internal reviews.** Each phase ran security-lens reviews; their findings are fixed (P1: 33 findings, one blocker).
4. **Patch releases.** A security fix ships as a patch version of the affected package (`packages/versions.json`, `packages/CHANGELOG.md`). The broken version is marked **Known insecure**, and the pack planner refuses to pin it. Forks take the patch through the feed (`tools/upstream-merge.ps1` and the package update runbook).

## Consequences

- Clients that require an independent review must commission one; the starter does not claim one.
- Any external review that is run later is recorded in a new ADR; its high and critical findings are fixed as patch releases before the next minor version.
- The DoD line "External review (A6 only): no open high or critical findings" is not applicable to v1.0.0.
