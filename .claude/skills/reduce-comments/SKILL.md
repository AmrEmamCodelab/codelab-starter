---
name: reduce-comments
description: Remove noise comments from ONE named class or file while provably changing no executable code - classify every comment as Remove, Simplify or Keep, edit comments only, then prove with the bundled comment-stripping differ (scripts/comment-diff.mjs) that the code is identical. Use when the user asks to reduce, clean up, trim, tidy or remove comments, says a class or file is over-commented or has obvious comments, or mentions reduce-comments. Works on C#, TypeScript, JavaScript, Angular HTML templates and SQL.
---

# Reduce comments

Delete comments that restate the code; keep every comment that says something the code cannot. **Change no
executable code, ever.** The deliverable is a diff in which nothing but comments moved, and a differ run that proves
it.

## The hard rule

Edit comment text only. Never change logic, names, signatures, SQL, conditions, formatting of code lines or member
order, and never "fix" anything you notice: list it under Observations in the report. If a comment cannot be removed
without touching its code line, keep it.

## Workflow

1. **Ask for the target, every time:** "Which class or file do you want me to clean?" Do not guess from context. If
   the user already named one, confirm it back before searching.
2. **Locate the declaration**, not just a file name:
   `rg -n "(class|interface|record|enum|struct)\s+<Name>\b" --glob '!**/{bin,obj,node_modules,dist}/**'`.
   One match: state the path. Several: list them numbered and ask. None: say so and ask again. One file per run.
   Read the whole file, and the interface or base class when a comment points at its contract.
3. **Classify every comment** with [REFERENCE.md](REFERENCE.md): **Remove** (restates the code), **Simplify** (true
   but five sentences for one fact), **Keep** (why, not what). When in doubt, keep. Never remove `TODO`, `FIXME`,
   `HACK`, `WARNING`, `SECURITY`, `IMPORTANT`, `NOTE:`, licence headers, generated-code markers or tool directives
   (`#pragma`, `eslint-disable`, `@ts-expect-error`, `/// <reference`). XML doc (`///`) and JSDoc (`/** */`) are
   keep-by-default: suggest, never delete.
4. **Count and report the plan, then proceed:**
   `node .claude/skills/reduce-comments/scripts/comment-diff.mjs count <path>`
   Report: target, comment units found, Remove / Simplify / Keep counts, one-line reason.
5. **Snapshot first** (the working tree may hold unrelated changes, so `git diff` alone cannot isolate your edit):
   copy `<path>` to a baseline file in the session scratchpad, keeping the extension. Then make small, precise
   comment edits. Deleting a whole-line comment deletes its line; do not leave a new blank line behind.
6. **Verify (mandatory):**
   `node .claude/skills/reduce-comments/scripts/comment-diff.mjs verify <baseline> <path>`
   - Exit 0, `PASS`: only comments changed.
   - Exit 1, `FAIL`: code changed. It prints the line in each file; revert that change at once and verify again.
     Never keep it because it looks like an improvement.
   - Exit 2: unsupported or unreadable file. Verify by careful reading instead and say so in the report.
   Then read `git diff -- <path>` and confirm every hunk is comment-only. Run the module's unit tests when cheap.
7. **Report:**

```text
Comment cleanup completed.
Target:                 <Name> (<path>)
Comments before/after:  <units> / <units>
Removed / simplified / kept: <n> / <n> / <n>
Code behaviour changed: No
Validation:             comment-diff verify PASS; git diff reviewed (comments only); tests: <result or "not run">
Observations (no action taken):
- <anything noticed but not touched>
```

If nothing warranted removal: "No unnecessary comments were found. No code changes were made."

## What the differ proves

`scripts/comment-diff.mjs` (zero-dependency Node ESM) scans both files per language, drops comments, collapses code
whitespace, keeps every string literal byte for byte, and compares. Strings holding `//`, `/*`, `--` or `<!--` are
code; so are C# verbatim, interpolated and raw strings, template literals with `${}` holes, regex literals, SQL
`'strings'` and `[identifiers]`, and HTML attribute values. Joining two code lines fails (it can change automatic
semicolon insertion). Anything the scanner is unsure of counts as code, so its mistakes are false alarms; the one
exception (a regex literal right after `)`) and the other limits are in [REFERENCE.md](REFERENCE.md#the-differ).
Tests: `node --test ".claude/skills/reduce-comments/scripts/tests/**/*.test.mjs"`.
