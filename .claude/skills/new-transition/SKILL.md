---
name: new-transition
description: "Add statuses and transitions to a record type's workflow: transition table entries with permission and reason requirements, status lookup IDs seeded by migration, audience-specific wording keys, TransitionFixture tests and the regenerated Mermaid diagram. Use when the work needs a new status, a new step in an approval flow, a new decision (approve, reject, return, escalate) or a changed status flow."
---

# New transition

**Status: planned for phase 7.** This skill is a stub so the routing table in the root `CLAUDE.md` resolves. Its full workflow is written when phase 7 delivers what it depends on.

## What it will produce

The skill will produce transition table entries, status ID constants and migration, wording keys, fixture tests and `docs/generated/workflows/` diagrams. Every scaffold emits the unit tests, the `module.rules.yaml` changes and the `MODULE.md` update together with the code.

## Until then

There is no workflow engine in phase 0. Tell the user that transition scaffolding arrives in phase 7.

Always load the `coding-standards` skill before any change.
