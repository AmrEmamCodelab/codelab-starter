---
name: new-permission
description: "Add a permission key to both the C# and the TypeScript permission catalogs, wire it to endpoints, routes and buttons, and regenerate docs/generated/permissions.md. Use when the work needs a new permission, a new access right, or gating an action, route or button for a role."
---

# New permission

**Status: planned for phase 2.** This skill is a stub so the routing table in the root `CLAUDE.md` resolves. Its full workflow is written when phase 2 delivers what it depends on.

## What it will produce

The skill will produce the key in both catalogs, its use on endpoints, routes and buttons, the regenerated permissions doc and tests. Every scaffold emits the unit tests, the `module.rules.yaml` changes and the `MODULE.md` update together with the code.

## Until then

There is no permission catalog in phase 0. Tell the user that permission scaffolding arrives in phase 2 (catalogs complete in phase 3).

Always load the `coding-standards` skill before any change.
