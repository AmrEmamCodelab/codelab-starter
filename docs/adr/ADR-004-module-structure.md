# ADR-004: Module structure (one project per module, IModule registration)

- **Status:** Accepted
- **Date:** 2026-11-04
- **Deciders:** Amr Emam (platform owner)

## Context

MODHS grew a single host with a 2,243-line `Program.cs`, divergent folder layouts and no architecture tests. The starter ships 16 generic modules plus a sample, and forks add and remove modules. Per-module guardrails (rules files, banned APIs, analyzers and docs) need a unit that the build tools can address.

## Decision

1. **Kernel:** four layer projects: `Starter.Domain`, `Starter.Application` (ports only, no EF), `Starter.Infrastructure` and `Starter.Api` (the host).
2. **Each backend module is one project**, `backend/src/Modules/<Name>/Starter.Modules.<Name>.csproj`, with the layer namespaces `.Domain`, `.Application`, `.Infrastructure` and `.Api` inside it. Next to the project sit `module.rules.yaml`, `MODULE.md`, a generated nested `CLAUDE.md` and a generated `BannedSymbols.generated.txt`.
3. **Registration goes through `IModule`.** The host discovers modules, and each module exposes `AddXxx`/`UseXxx`. Adding or removing a module never edits `Program.cs`, which stays at 150 lines or fewer; an architecture test asserts this.
4. **Frontend modules** are folders under `frontend/starter-web/src/app/features/`, each with its own `module.rules.yaml`, `MODULE.md` and `CLAUDE.md`.
5. `tools/remove-module.ps1` edits only the `.slnx` and the host's project reference list.

## Consequences

- Per-project analyzers and banned symbols apply per module. The architecture tests (ADR-011) check layer rules inside a module by namespace, and module dependencies by assembly.
- There are fewer projects than with one project per layer per module, so the solution loads and builds faster.
- Layering inside a module is enforced by tests, not by project references, so the architecture tests are mandatory.
