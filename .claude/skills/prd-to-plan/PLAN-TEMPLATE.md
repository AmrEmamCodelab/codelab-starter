# Plan template

Copy into `plans/<slug>.md`. Keep each phase a vertical slice; describe behaviour end to end, not layer by layer.

```markdown
# Plan: <Feature or initiative name>

> Source PRD: [plans/<slug>-prd.md](<slug>-prd.md)
> Status: draft | approved | in progress | done
> Tracker: none | <item link>

## Architectural decisions

Durable decisions that apply to every phase:

- **Modules**: <new or changed modules, e.g. `Starter.Modules.<Name>`>
- **Record types and statuses**: <names, the transition table lives in the feature spec>
- **Routes**: <API routes and screen routes>
- **Schema**: <tables and key columns; enums stored as integers with explicit values>
- **Permissions**: <dotted permission keys>
- (add or remove rows as needed)

## Assumptions and open questions

1. <decision taken without confirmation, or a gap the PRD leaves open>

---

## Phase 1: <Title>

**User stories**: <numbers from the PRD>
**Feature spec**: <docs/features/<feature>.md, or "none" for non-scenario work>
**Skills**: <in order, e.g. `new-module` -> `new-permission` -> `new-endpoint` -> `new-screen`>

### What to build

<The end-to-end behaviour of this slice.>

### Acceptance criteria

- [ ] <observable behaviour>
- [ ] Unit tests for every use case, validator and transition of the slice
- [ ] MODULE.md of every touched module updated
- [ ] `tools/build.ps1 -Target CI` green

---

## Phase 2: <Title>

<!-- Repeat for each phase -->
```
