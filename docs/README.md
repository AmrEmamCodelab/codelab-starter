# docs/

## Purpose

Human- and agent-readable documentation for the starter: standards and decisions. Module-specific docs live next to each module as `MODULE.md`, not here.

## Use

| Folder | Read it when |
| --- | --- |
| [standards/](standards/SKILL.md) | You are about to implement or review anything. The `coding-standards` skill loads it. |
| [adr/](adr/README.md) | You need to know why the repo is shaped the way it is, or you are about to change a structural decision. |

Later phases add `recipes/` (how to add an endpoint, permission, workflow type, page, integration, notification or lookup), `runbooks/`, `design/` and `generated/` (drift-checked output; never hand-edited).

## Extend

- New standard: add it to `standards/SKILL.md` with a number, add its row, a BAD/GOOD example and a red flag to `standards/EXAMPLES.md`, and, if it can be machine-checked, a rule in `rules/base.rules.yaml`.
- New decision: add `adr/ADR-0NN-<slug>.md` (Status, Date, Deciders, Context, Decision, Consequences) and a row in `adr/README.md`. Supersede rather than rewrite an accepted ADR.

## Rules

- Starter names only. Everything under `docs/` except `adr/` is covered by the CI term sweep.
- The build plan and the decision log are platform-team inputs and stay outside the repository (every fork inherits what is in it); their outcomes are recorded as ADRs.
- Every link and file reference must resolve; the docs gate fails on dead references.
- Changes under `docs/standards/` change what every review enforces; ask the platform owner.

## Gotchas

- Type names in `standards/` are the target contract of the plan; much of the code arrives in phases 1 to 7. Verify a name in code before copying it.
