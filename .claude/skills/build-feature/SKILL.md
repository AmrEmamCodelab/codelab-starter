---
name: build-feature
description: "Turn a business scenario into a reviewed, tested, documented feature: decompose it into modules, statuses and transitions, permissions, endpoints and screens, write a feature spec for human approval, then run the matching scaffold skills. Use whenever the user describes something the business needs (\"we need...\", \"clients should be able to...\", \"the reviewer approves...\"), or provides a BRD, a use-case workbook, a DGA screenshot or a Figma frame, even when no slash command is named. This is the default entry point for new functionality."
---

# Build feature

**Status: planned for phase 9.** This skill is a stub so the routing table in the root `CLAUDE.md` resolves. Its full workflow is written when phase 9 delivers what it depends on.

## What it will produce

The skill will produce a feature spec in `docs/features/` (approved by a human), then backend, frontend, tests and docs through the scaffold skills and the builder and reviewer agents. Every scaffold emits the unit tests, the `module.rules.yaml` changes and the `MODULE.md` update together with the code.

## Until then

Write a short spec in chat listing the modules, statuses and transitions, permissions, endpoints and screens the scenario needs, get the user's approval, then use `do-work` for each step.

Always load the `coding-standards` skill before any change.
