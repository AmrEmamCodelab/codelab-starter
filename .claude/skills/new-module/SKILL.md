---
name: new-module
description: "Scaffold a new backend module (Starter.Modules.<Name>) with the full anatomy: csproj, IModule registration, permission keys and catalog source, error codes, routes, PublicId domain, module.rules.yaml, MODULE.md, the generated nested CLAUDE.md, a unit-test project, the slnx entries and the host ProjectReference. Use when the work needs a new business area, record type or platform area that does not belong to an existing module."
---

# New module

Load the `coding-standards` skill first ([docs/standards/SKILL.md](../../../docs/standards/SKILL.md)).
Never create a module folder by hand: the generator is the only sanctioned way to edit the solution and the
host project's module list.

## Quick start

From the repo root, with the private Node on `PATH`:

```powershell
$env:PATH = "E:\codelab-tools\node24;$env:PATH"
node tools/scaffold/new-module.mjs --name Records
```

`--name` is PascalCase (`ServiceRequests`). The command refuses (exit 1, nothing written) a non-Pascal name, an
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
