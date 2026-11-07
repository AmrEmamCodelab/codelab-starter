---
name: prd-to-plan
description: Turn an existing PRD (product requirements document) into a phased implementation plan of tracer-bullet vertical slices, each phase naming the starter skills that build it, saved as a tracked Markdown file plans/<slug>.md. Use when the user has a PRD (pasted, or plans/<slug>-prd.md) and wants it broken down into phases, an implementation plan, a roadmap of slices or "tracer bullets". With no PRD yet, use prd-and-plan; for one business scenario to build now, use build-feature.
---

# PRD to plan

Break a PRD into thin end-to-end phases. The plan is written to `plans/<slug>.md` and is **tracked in git**: it is
reviewed in the pull request like code ([plans/README.md](../../../plans/README.md)).

## Workflow

1. **Get the PRD.** It is in the conversation or at `plans/<slug>-prd.md`. If there is none, ask for it, or offer the
   `prd-and-plan` skill to write both. Reuse the PRD's slug for the plan.
2. **Explore the code** the PRD touches: read the root `CONTEXT.md` (module index, use-case tracing), the
   `MODULE.md` of every module involved, and the sample module `backend/src/Modules/ServiceRequests` when the work
   adds a record type.
3. **Fix the durable decisions** for the plan header: routes, record types and statuses, schema shape, permission
   keys, module boundaries. Leave out file and method names; they churn.
4. **Slice into tracer bullets.** Each phase is a narrow but complete path through every layer (migration, use case,
   endpoint, screen, tests, docs) that can be demonstrated on its own. Prefer many thin phases to a few thick ones.
   Map each phase to the starter skills that build it (table below), in the order they run.
5. **Quiz the user.** Show the phases as a numbered list (title, PRD user stories covered, skills). Ask whether the
   granularity is right and whether any phase should be merged or split. Iterate until approved.
6. **Write `plans/<slug>.md`** from [PLAN-TEMPLATE.md](PLAN-TEMPLATE.md). Link the PRD and, per phase, the feature
   spec (`docs/features/<feature>.md`) when the phase builds a business scenario.
7. **Tracker (optional).** If the `Issue tracker` section of `plans/README.md` names a tracker and its CLI is set up,
   offer to create one item per phase that links the plan. Never store or echo tracker credentials.

## Skills a phase can name

| The phase needs... | Skill or recipe |
| --- | --- |
| A business scenario (new record type, statuses, screens) | `build-feature` (writes and gets approval for `docs/features/<feature>.md`) |
| A new business area or record type | `new-module` |
| A new status or status flow | `new-transition` |
| A time limit, deadline or SLA | recipe [docs/recipes/add-deadline.md](../../../docs/recipes/add-deadline.md) |
| A new permission | `new-permission` |
| A new action or API operation | `new-endpoint` |
| A new page | `new-screen` (+ the page patterns in [docs/design/](../../../docs/design/README.md)) |
| A new reference-data list | `new-lookup` |
| Anything already specified (a fix, a refactor, a step) | `do-work` |

Every phase is later executed with `do-work` (or `build-feature` for a scenario), with the `coding-standards` skill
loaded. Acceptance criteria name the checks that prove the slice: unit tests in
`backend/tests/Modules/Starter.Modules.<Name>.Tests`, HTTP or real-SQL tests in
`backend/tests/Starter.Integration.Tests`, Vitest specs in `frontend/starter-web`, and the docs gate.

## Rules

- One plan per PRD, same slug: `plans/<slug>-prd.md` and `plans/<slug>.md`.
- Never put a plan under a gitignored folder, in `.claude/`, or outside the repo.
- Starter names only; no secrets, hostnames or credentials in a plan.
- When a phase is done, tick its acceptance criteria in the plan in the same change.
