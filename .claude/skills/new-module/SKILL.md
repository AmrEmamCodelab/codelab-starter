---
name: new-module
description: "Scaffold a new backend module project and its frontend feature folder with module.rules.yaml, MODULE.md, the generated nested CLAUDE.md, IModule registration and starter tests. Use when the work needs a new business area or a new platform area that does not belong to an existing module."
---

# New module

**Status: planned for phase 2.** This skill is a stub so the routing table in the root `CLAUDE.md` resolves. Its full workflow is written when phase 2 delivers what it depends on.

## What it will produce

The skill will produce `backend/src/Modules/<Name>/Starter.Modules.<Name>.csproj` with `.Domain/.Application/.Infrastructure/.Api` namespaces, `module.rules.yaml` from the template, `MODULE.md`, the nested `CLAUDE.md`, the `.slnx` entry and a test project. Every scaffold emits the unit tests, the `module.rules.yaml` changes and the `MODULE.md` update together with the code.

## Until then

There is no module code in phase 0. Do not create a module folder by hand; tell the user that module scaffolding arrives in phase 2 (ADR-004 describes the structure).

Always load the `coding-standards` skill before any change.
