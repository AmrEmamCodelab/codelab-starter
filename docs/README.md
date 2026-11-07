# docs/

## Purpose

Human- and agent-readable documentation for the starter: standards and decisions. Module-specific docs live next to each module as `MODULE.md`, not here.

## Use

| Folder | Read it when |
| --- | --- |
| [standards/](standards/SKILL.md) | You are about to implement or review anything. The `coding-standards` skill loads it. |
| [adr/](adr/README.md) | You need to know why the repo is shaped the way it is, or you are about to change a structural decision. |
| [recipes/](recipes/README.md) | You are adding an endpoint (step-by-step recipes). |
| [generated/](generated/README.md) | You need the current permission, endpoint, error-code or config-key list (derived; never hand-edited). |
| [runbooks/](runbooks/README.md) | You operate a deployed environment (Seq, gateway, key rotation, CSP enforcement, certificate renewal, upstream merge). |
| [ai-kit/](ai-kit/README.md) | You run or change the AI development kit: the 16 agents, the build-feature steps they run, and the knowledge every builder and reviewer reads ([knowledge-wiring.md](ai-kit/knowledge-wiring.md)). |
| [features/](features/README.md) | You write or review a feature spec: the format ([TEMPLATE.md](features/TEMPLATE.md)), the human approval gate, the lifecycle and a worked example ([examples/site-visit-permit.md](features/examples/site-visit-permit.md)); validated by `node tools/feature-spec/check-spec.mjs`. |
| [plans/](../plans/README.md) | You plan work larger than one feature: tracked PRDs and phased plans, and how they relate to `docs/features/`. |
| [tools/ai-evals](../tools/ai-evals/MODULE.md) | You run or change the kit evals (fixtures, headless runner, grader, pass-rate gate, schedule). |

Later phases add more recipes (permission, workflow type, page, integration, notification, lookup), `runbooks/` and `design/`.

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
