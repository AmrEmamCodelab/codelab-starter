---
name: write-a-skill
description: Create or change a Claude Code skill in codelab-starter with the right structure, a trigger-accurate description, progressive disclosure and bundled resources. Use when the user wants to create, write, build or improve a skill, or when a scaffold skill planned in the CLAUDE.md routing table is being implemented.
---

# Writing skills

Skills live in `.claude/skills/<name>/`. Changes under `.claude/` need platform-owner review (CODEOWNERS), and every skill file is covered by the CI term sweep, so use starter names only.

## Process

1. **Gather requirements.** Ask the user (skip what is already known):
   - Which task or domain does the skill cover, and which use cases must it handle?
   - Does it need executable scripts, or only instructions?
   - Which reference material (standards, recipes, MODULE.md files) should it point to?
2. **Draft the skill:**
   - `SKILL.md` with concise instructions.
   - Reference files when the content would push `SKILL.md` past 100 lines.
   - Utility scripts for deterministic operations (Node `.mjs`, with `*.test.mjs` tests).
3. **Wire it in.** If the skill serves an intent, add or update its row in the routing table in the root `CLAUDE.md`. If it scaffolds code, it must emit the unit tests, the `module.rules.yaml` changes and the `MODULE.md` updates together with the code.
4. **Review with the user:** does it cover the use cases, is anything missing or unclear, should any section be shorter or longer?

## Skill structure

```
skill-name/
├── SKILL.md           # main instructions (required)
├── REFERENCE.md       # detailed docs (if needed)
├── EXAMPLES.md        # usage examples (if needed)
└── scripts/           # utility scripts (if needed)
    ├── helper.mjs
    └── helper.test.mjs
```

## SKILL.md template

```md
---
name: skill-name
description: What it does, in one sentence. Use when [specific triggers].
---

# Skill name

## Quick start

[Minimal working example]

## Workflow

[Step-by-step process with a checklist for complex tasks]

## More

[Links to REFERENCE.md or EXAMPLES.md, one level deep]
```

## Description requirements

The description is **the only thing the agent sees** when it decides which skill to load. It appears in the system prompt next to every other installed skill, and the agent matches the user's request against it. Routing in this repo depends on it: the developer describes a business scenario, and the description must make the right skill load without a slash command.

Give the agent just enough to know:
1. what capability the skill provides, and
2. when to trigger it (keywords, contexts, file types, business situations).

Format:
- At most 1024 characters, written in the third person.
- First sentence: what it does. Second sentence: "Use when [specific triggers]".

Good: `Scaffold a new backend endpoint with its permission, validator, PublicId binding and tests. Use when the work needs a new action or API operation on an existing module.`

Bad: `Helps with endpoints.` The agent cannot tell it apart from the other scaffold skills.

## When to add scripts

Add a script when the operation is deterministic (validation, formatting, generation), when the same code would otherwise be generated repeatedly, or when errors need explicit handling. Scripts save tokens and are more reliable than generated code. Follow the coding standards: no magic strings or numbers, small functions, tests for logic.

## When to split files

Split when `SKILL.md` exceeds 100 lines, when the content has distinct domains, or when advanced features are rarely needed.

## Review checklist

- [ ] The description says what it does and includes "Use when..." triggers.
- [ ] `SKILL.md` is under 100 lines.
- [ ] No time-sensitive information, and no machine-specific paths unless the repo docs already state them.
- [ ] Terminology matches the glossary in the root `CONTEXT.md`.
- [ ] Concrete examples are included.
- [ ] References are one level deep and every linked path exists.
- [ ] The routing table in the root `CLAUDE.md` is updated if the skill serves an intent.
