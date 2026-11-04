---
name: new-endpoint
description: "Scaffold a new API endpoint in an existing module with its use case, [HasPermission] key, PublicId route binding, validator, problem+json errors, unit and contract tests, and the regenerated TypeScript client. Use when the work needs a new action, command, query or API operation on an existing module."
---

# New endpoint

**Status: planned for phase 2.** This skill is a stub so the routing table in the root `CLAUDE.md` resolves. Its full workflow is written when phase 2 delivers what it depends on.

## What it will produce

The skill will produce the endpoint, its Application use case, validator, permission key, unit tests and contract-test coverage, plus `docs/recipes/add-endpoint.md` guidance. Every scaffold emits the unit tests, the `module.rules.yaml` changes and the `MODULE.md` update together with the code.

## Until then

There is no API host in phase 0. Tell the user that endpoint scaffolding arrives in phase 2.

Always load the `coding-standards` skill before any change.
