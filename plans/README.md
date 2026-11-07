# Plans

This folder holds product requirement documents (PRDs) and phased implementation plans. **Plans are tracked in
git**: they are reviewed in the pull request with the code they lead to, they survive a fresh clone, and a fork
inherits none of them by accident because each project writes its own. (The source product kept its plans in a
gitignored folder; the starter fixed that in phase 0, see the note on line 1 of `.gitignore`.)

| File | Written by | What |
| --- | --- | --- |
| `<slug>-prd.md` | `prd-and-plan` skill (or by hand) | Why and what: problem, user stories, decisions, test seams, open questions |
| `<slug>.md` | `prd-to-plan` or `prd-and-plan` skill | How, in order: durable decisions, then thin tracer-bullet phases with acceptance criteria |

The skills: [prd-to-plan](../.claude/skills/prd-to-plan/SKILL.md) (an existing PRD to a plan, with a review of the
phase breakdown) and [prd-and-plan](../.claude/skills/prd-and-plan/SKILL.md) (both documents in one run, with
assumptions recorded instead of questions asked).

## Plans and feature specs

A **feature spec** in [docs/features/](../docs/features/README.md) is the build contract for **one business
scenario**: the `build-feature` skill writes it, a human approves it, and only then do the scaffold skills create
code. It lists exactly the modules, transitions, permissions, endpoints, screens, tests and docs.

A **plan** sits one level above: it orders work that is bigger than one scenario, or that is not a scenario at all
(a group of bug fixes, a refactor, a platform change). Each plan phase names:

- the feature spec it builds through `build-feature`, when the phase adds business functionality, or "none";
- the starter skills it runs, in order (`new-module`, `new-transition`, `new-permission`, `new-endpoint`,
  `new-screen`, `new-lookup`, the deadline recipe [docs/recipes/add-deadline.md](../docs/recipes/add-deadline.md),
  or `do-work` for work that is already specified);
- acceptance criteria that end in green checks (unit tests, integration tests, docs gate, CI).

A PRD never replaces the approval of a feature spec. A small change needs neither: use `do-work` directly.

| You have... | Use |
| --- | --- |
| One business scenario to build ("clients should be able to...") | `build-feature` (spec in `docs/features/`) |
| A PRD to break into phases | `prd-to-plan` |
| Notes or tracker items, and want a PRD and a plan | `prd-and-plan` |
| A planned phase or a specified fix to execute | `do-work` |

## Lifecycle

1. Write the PRD and plan; commit them with status `draft`.
2. Review them in a pull request like code; set status `approved`.
3. While executing, tick each phase's acceptance criteria in the same change that completes the phase.
4. When every phase is done, set status `done`. Keep the files: they record why the code looks the way it does.

## Issue tracker

The planning skills read this section to decide whether to pull work items or publish PRDs and phases. A fork sets
it once; the starter ships with none.

```text
Tracker: none
```

To use one, replace `none` with the tracker and the command-line tool the skills should call (for example
`GitHub Issues via gh`), and add the label to put on published PRDs (for example `ready-for-agent`). Never write
server addresses with credentials, tokens or passwords here: credentials are supplied at run time and never stored.

## Rules

- No secrets, credentials, internal hostnames or personal data in a PRD or plan.
- Starter names only in the starter; a fork uses its own names after `init.ps1`.
- Describe modules and responsibilities, not file paths and method names; those churn between phases.
