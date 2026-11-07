---
name: new-module
description: "Use when the work needs a new business area or record type, or any platform area that does not belong to an existing backend module (for example 'clients file complaints', 'we need permits'), and whenever a hook or CI refusal says 'Use the `new-module` skill' (a module folder without module.rules.yaml). Scaffolds Starter.Modules.<Name> with the full anatomy in one run: csproj, IModule registration, permission keys and catalog source, error codes, routes, PublicId domain, module.rules.yaml, MODULE.md, the generated nested CLAUDE.md, a unit-test project, the slnx entries and the host ProjectReference, then refreshes the rules compile and docs/generated."
---

# New module

Load the `coding-standards` skill first ([docs/standards/SKILL.md](../../../docs/standards/SKILL.md)).
Never create a module folder by hand: the generator is the only sanctioned way to edit the solution and the
host project's module list.

## When this applies

- The scenario names a new business area or record type (a complaint, a permit), or a platform area no module owns.
- **Refusal counterpart:** the PreToolUse hook (`.claude/hooks/pre-tool-use.mjs`, rule `hooks.module-without-rules`)
  and the CI content check (`node tools/rules-engine/check-content.mjs`) refuse code in a module folder that has no
  `module.rules.yaml`, with the message "Use the `new-module` skill". When you see it, stop writing files by hand and
  run this skill; a frontend feature folder gets the same refusal naming `new-screen` instead.
- Not this skill: an action on an existing module (`new-endpoint`), a status flow (`new-transition`), a reference list
  (`new-lookup`).

## What one run emits

| Part | Emitted |
| --- | --- |
| Code | yes: csproj, `<Name>Module`, registration, `<Name>PermissionKeys` (`<name>.view`), catalog source, error codes, routes, ids, limits, slnx + host reference |
| Rules | yes: `module.rules.yaml` (rule ids `<lower-case name>.thin-endpoints`, `.data-through-ports`, recipe `add-endpoint`), then the nested `CLAUDE.md` through the rules compile |
| MODULE.md | yes: the five sections (Purpose, Use, Extend, Rules, Gotchas); you replace the Purpose paragraph |
| Unit tests | yes: `backend/tests/Modules/Starter.Modules.<Name>.Tests` with `<Name>ModuleTests` |
| Generated docs | rerun automatically (`node tools/rules-engine/compile.mjs`, `Starter.DocsGen`); with `--skip-generate` or `--dry-run` it prints them as `next:` lines |

## Quick start

From the repo root, with the private Node on `PATH`:

```powershell
$env:PATH = "E:\codelab-tools\node24;$env:PATH"
node tools/scaffold/new-module.mjs --name Records
```

Add `--dry-run` first to see every file it would write. `--name` is PascalCase (`ServiceRequests`). The command refuses (exit 1, nothing written) a non-Pascal name, an
existing module, a kernel or reserved name (`Domain`, `Application`, `Infrastructure`, `Api`, `Shared`, ...) and a
name listed in `codelab.starter.json` `removedModules`. It prints every file it created or changed, then reruns
`node tools/rules-engine/compile.mjs` and `Starter.DocsGen`.

## Workflow

1. Run the command above. Read the printed list; nothing else changed.
2. Hand-fill only what is business-specific:
   - `MODULE.md`: replace the Purpose paragraph with the business area; keep all five sections
     (Purpose, Use, Extend, Rules, Gotchas) current with every later change.
   - `module.rules.yaml`: the `description`, any `mustNotDependOn` module and module-specific rules; rerun
     `node tools/rules-engine/compile.mjs` after editing it.
   - `Domain/<Name>Limits.cs`: the real limits.
3. Add endpoints with the `new-endpoint` skill and permissions with the `new-permission` skill. Add a DbContext
   (schema = the kebab name, one `InitialCreate`) only when the module stores data; see
   [the module anatomy](../../../backend/src/Modules/README.md).
4. Extend `backend/tests/Modules/Starter.Modules.<Name>.Tests/<Name>ModuleTests.cs` with every domain rule you add.

## Verify

```powershell
$dotnet = "E:\codelab-tools\dotnet10\dotnet.exe"
& $dotnet build backend/src/Starter.Api/Starter.Api.csproj -warnaserror
& $dotnet test backend/tests/Modules/Starter.Modules.<Name>.Tests
& $dotnet test backend/tests/Starter.Architecture.Tests
& $dotnet test backend/tests/Starter.Contract.Tests
node tools/rules-engine/compile.mjs --check
& $dotnet run --project backend/tools/Starter.DocsGen -- --out docs/generated --check
& $dotnet run --project tools/docs-gate -- check --root .
```

The full gate is `tools/build.ps1 -Target CI`. To remove a module later: `tools/remove-module.ps1 -Name <Name>`.

## More

- Generator details, flags and exit codes: [tools/scaffold/README.md](../../../tools/scaffold/README.md).
- Module layout and conventions: [backend/src/Modules/README.md](../../../backend/src/Modules/README.md).
