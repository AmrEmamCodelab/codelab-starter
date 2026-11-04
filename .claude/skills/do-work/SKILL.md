---
name: do-work
description: Execute a unit of work end to end in codelab-starter or a fork of it - understand, plan, implement test-first, validate with tools/build.ps1, update MODULE.md, then commit when asked. Use when the user wants to do work, build or change something already specified, fix a bug, or implement a phase or step from a plan. For a new business scenario that is not yet specified, use build-feature instead.
---

# Do work

Execute one complete unit of work: understand it, plan it, build it test-first, validate it, document it.

Load the `coding-standards` skill first; it applies to every step below.

## 1. Understand the task

Read the referenced plan, spec or issue, the root `CONTEXT.md`, and the `MODULE.md` and nested `CLAUDE.md` of every module involved. Explore the relevant code, callers, contracts and tests. If the scope is ambiguous in a way that changes the result, ask before proceeding.

## 2. Plan

If the task is not already planned, write a short plan: the slices, the files and modules touched, the rules files affected, and how each slice is verified. When the work needs a new module, endpoint, screen, permission, transition or lookup, use the matching scaffold skill from the routing table in the root `CLAUDE.md`.

## 3. Implement test-first (backend and frontend)

Unit tests are mandatory for every piece of logic, on both sides. Use red/green/refactor, one thin vertical slice at a time:

1. Write one failing test for the smallest slice of behaviour (xUnit for .NET, Vitest for Angular).
2. Run it and confirm it fails (red).
3. Write the minimum code to make it pass (green).
4. Repeat for the next slice. Refactor while the tests stay green.

Do not write all the tests up front. Templates, styles and pure markup need no test of their own, but any logic behind them does.

## 4. Validate

Run the feedback loops and fix every failure; repeat until clean.

```powershell
# Focused loop while working (the .NET 10 SDK is not on PATH on this machine)
E:\codelab-tools\dotnet10\dotnet.exe build codelab-starter.slnx -warnaserror
E:\codelab-tools\dotnet10\dotnet.exe test codelab-starter.slnx

# Before finishing: the full CI contract
powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Target CI
```

Never edit generated files to make a check pass (`rules/rules.compiled.json`, nested `CLAUDE.md`, `BannedSymbols.generated.txt`, `docs/generated/`); fix the source and regenerate.

## 5. Document

Update the `MODULE.md` of every module you changed (Purpose, Use, Extend, Rules, Gotchas). If no doc change is needed, the commit message must carry `Docs-Not-Needed: <reason>`, or the docs gate fails.

## 6. Commit

Commit only when the user asked for it. Use a clear message that says what changed and why, plus the `Docs-Not-Needed:` trailer when it applies. Never commit with a red CI, never skip hooks, and never commit secrets or key material.

Report what changed, how it was verified and any remaining limitation.
