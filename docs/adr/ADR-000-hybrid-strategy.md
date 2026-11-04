# ADR-000: Hybrid strategy (packages for the core, source for the rest)

- **Status:** Accepted
- **Date:** 2026-11-04
- **Deciders:** Amr Emam (platform owner)

## Context

Codelab builds Ministry and KSA-government request portals: Arabic-first RTL, the DGA look, on-premises Windows Server/IIS with SQL Server, and a backend zone with no internet egress. MODHS, a Codelab-owned portal built with Claude Code, already contains most of what every such project needs: a security core (request signing, opaque IDs, replay guards, headers), observability, error handling, identity, authorization, a workflow engine, notifications, attachments, portal pages and a DGA component set. Starting each new project by copying MODHS by hand would copy its defects and its domain, and fixes would never flow between projects.

Two pure models were rejected:
- **Everything as packages.** Workflow, records and screens are reshaped by every project; a package boundary there would force forks to fight the API.
- **Everything as source (template only).** Security-critical code would drift per project, and every fix would need a manual port into every fork.

## Decision

`codelab-starter` follows a **hybrid** model (decision D3):
1. The **stable, security-critical core** ships as versioned packages: NuGet `Codelab.Security`, `Codelab.Observability`, `Codelab.Errors`, `Codelab.Integration`, `Codelab.Identity`, `Codelab.Build.Sdk` and `Codelab.Testing`; npm `@codelab/ng-security`, `@codelab/ng-dga` and `@codelab/ng-tools`. Forks receive fixes by a version bump.
2. **Everything else is source** in the starter: the kernel, the 16 generic modules, layouts, page patterns, portal pages and the sample `ServiceRequests` module. Projects fork the starter and take source fixes with `tools/upstream-merge.ps1` (ADR-003).
3. `Codelab.Notifications` and `@codelab/ng-core` stay as source until a second project proves their API.
4. Codelab owns and maintains the starter through a named platform owner (D4: Amr Emam). MODHS is the harvest source only and is never modified (D6).
5. The design system is owned: there is no external DGA npm package. `@codelab/ng-dga` holds the components, style foundation, fonts and icon font (D5).

## Consequences

- There are two release mechanisms to run: package versions on the feed (ADR-002) and source merges from upstream (ADR-003).
- Package APIs need compatibility discipline. PublicApiAnalyzers and package validation in Codelab.Build.Sdk fail a breaking change at pack time.
- Every project starts from a working, branded, secure request system and adds only its business rules.
- The starter must stay AI-native, because projects are built with Claude Code. Rules files, hooks, a docs gate, scaffold skills and CI guardrails exist from phase 0 (ADR-006).
