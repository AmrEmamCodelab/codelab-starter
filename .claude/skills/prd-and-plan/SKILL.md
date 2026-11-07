---
name: prd-and-plan
description: Write a PRD and its phased tracer-bullet implementation plan in one run, without stopping for interviews or approvals, to the tracked files plans/<slug>-prd.md and plans/<slug>.md. The source is feature notes from the conversation or, when a tracker is configured in plans/README.md, the work items assigned to a developer. Use when the user wants both a PRD and a plan produced together ("write a PRD and plan for...", "plan the bugs assigned to me"), or wants a larger initiative spanning several features planned. To plan an existing PRD, use prd-to-plan; to build one business scenario now, use build-feature.
---

# PRD and plan (one run)

Produce two tracked documents back to back: `plans/<slug>-prd.md` (the PRD) and `plans/<slug>.md` (the plan). See
[plans/README.md](../../../plans/README.md) for why plans are tracked and how they relate to `docs/features/`.

**Run end to end.** The interactive checkpoints of `prd-to-plan` (quiz the user, iterate until approved) are
skipped. Decide from the context and the code, and record every unconfirmed decision and every material gap in a
numbered **Assumptions and open questions** section in both files. Never invent requirements the source does not
support; list them as open questions instead.

## Workflow

0. **Tracker mode only: gather the work items.** Skip when the user gave feature notes. Read the `Issue tracker`
   section of `plans/README.md`. If it says `none`, say so and ask for the items as text. Otherwise use the tool it
   names, read-only, for the items assigned to the developer named (default: the current user), keeping open
   states only (state the filter). Credentials are supplied by the user at run time; never store, echo or log them.
   The items are the requirements: cite each by its id. Items from clearly different areas get one PRD and plan
   pair per area, each with its own slug; say how you grouped them.
1. **Slug.** A short kebab-case name for the area (`site-visit-permit`, `queue-reassign-fixes`), shared by both files.
2. **Explore the code.** Read the root `CONTEXT.md`, the `MODULE.md` of each module involved and the generated lists
   in `docs/generated/` (permissions, endpoints, error codes). Use the glossary terms (record, transition, lookup,
   permission key, refusal, PublicId).
3. **Choose the test seams** (decide, do not ask): use cases and validators in
   `backend/tests/Modules/Starter.Modules.<Name>.Tests`; HTTP and real-SQL behaviour in
   `backend/tests/Starter.Integration.Tests`; component and service logic in Vitest specs under
   `frontend/starter-web`. Prefer an existing seam at the highest level that proves the behaviour.
4. **Write the PRD** to `plans/<slug>-prd.md` from [PRD-TEMPLATE.md](PRD-TEMPLATE.md).
5. **Write the plan** to `plans/<slug>.md` with the plan template and the skill mapping of the `prd-to-plan` skill
   ([PLAN-TEMPLATE.md](../prd-to-plan/PLAN-TEMPLATE.md)): durable decisions in the header, then thin vertical
   phases, each naming its feature spec (when it builds a business scenario) and its skills in order.
6. **Tracker (optional).** When a tracker is configured, offer to publish the PRD and one item per phase that link
   the files. Do not publish without the user's yes.

## Summary to report

- Tracker mode: who was queried, the state filter, how many items, how they were grouped (ids per group).
- The files written, the slug and feature name, the phase titles (one line each), the test seams chosen.
- **The top open questions**, prominently: they were decided without confirmation.

## Rules

- Both files go to `plans/` and are committed with the work; never to `.claude/` or a gitignored folder.
- A phase that adds a business scenario is built through `build-feature`, whose spec in `docs/features/` needs a
  human approval before code; the PRD does not replace that approval.
- Starter names only; no secrets, hostnames, credentials or tracker tokens in either file.
