---
name: usecase-testing
description: "Test one use case end to end against its High Level Scenarios (HLS) workbook and report the result: reads the workbook with a dependency-free xlsx reader, derives scenarios and business rules, discovers the repo's test infrastructure (xUnit, Vitest, Playwright MCP, a run-local -Slot stack on its own throwaway database), runs happy, negative, validation, boundary, authorization, API, database, workflow and UI checks, traces every failure to file and line, and writes an .xlsx report or a Markdown bug report ready for prd-and-plan. Use when the user asks to test, verify, validate, QA or investigate a use case by its ID (UC-001, UC016, uc 20) or hands over a use-case or user-story workbook (.xlsx) to check the app against."
---

# Use-case testing

One use case, tested against its workbook, ending in a report. Load the `coding-standards` skill first
([docs/standards/SKILL.md](../../../docs/standards/SKILL.md)); depth, the workbook shapes, the starter's test map, the
`results.json` contract and the rubrics are in [REFERENCE.md](REFERENCE.md).

**Input:** a use-case ID and the HLS workbook path. Without a path, list candidate workbooks
(`git ls-files "*.xlsx"`, plus untracked files the user names) and ask. Never guess the use case, the workbook or its
version. **Output:** `test-reports/<UC-ID>/` with the workbook dump, `results.json` and the report.

## Two rules above all

1. **The workbook is the oracle.** Where the app and the workbook disagree, that is a finding, however deliberate the
   code looks. Never reconstruct a specification from the code under test.
2. **A gap is not a defect.** A row tagged as a gap (`[G-01]` in Notes, a gap column, "undefined" expected outcome) is
   an open spec question: record what the app does today, keep it out of the issues.

## Workflow

- [ ] **0. Inspect the repo.** Confirm the stack, test projects, commands, database and auth model from the repo itself
      ([CONTEXT.md](../../../CONTEXT.md) Commands, `docs/generated/`), not from memory. Record each as Evidence
      `Environment`. The repo wins over this file.
- [ ] **1. Read the workbook.** Dump it, keep the dump as evidence, then read every sheet (match sheets and columns by
      header text, never by position):

      ```powershell
      $uc = 'UC-001'; New-Item -ItemType Directory -Force "test-reports/$uc" | Out-Null
      powershell -NoProfile -ExecutionPolicy Bypass -File .claude/skills/usecase-testing/scripts/Read-Xlsx.ps1 -Path "<workbook.xlsx>" -OutFile "test-reports/$uc/workbook-dump.txt"
      powershell -NoProfile -ExecutionPolicy Bypass -File .claude/skills/usecase-testing/scripts/Read-Xlsx.ps1 -Path "<workbook.xlsx>" -Sheet BusinessRules -AsJson -OutFile "test-reports/$uc/BusinessRules.json"
      ```

      Record the workbook path and its version cell. Extract actors, preconditions, flows, rules, statuses and
      transitions, messages, fields and limits, each with its sheet and row.
- [ ] **2. Plan.** Every scenario row becomes a test case that keeps its workbook ID verbatim; copy the expected outcome
      before running anything. Cover the dimensions in REFERENCE.md "Test dimensions"; one that does not apply gets a
      `Blocked` row with the reason. Every testable rule maps to at least one test case or a `Blocked` row.
- [ ] **3. Run what exists, then fill gaps.** Search tests by scenario ID, rule ID, endpoint, permission key and record
      type; run the narrowest filter first (REFERENCE.md "Starter test map"). Add missing tests to the existing
      projects in their conventions. Never change production code to make a test pass.
- [ ] **4. Live checks on your own stack only.** `run-local.ps1 -Slot <n>` with `ConnectionStrings__Starter` on a
      throwaway database on a named LocalDB or SQL instance; never a running owner stack or its database. API, database
      and UI checks (Playwright MCP, ar and en, RTL) go against that slot. Stop it with `-Stop -Slot <n>` and drop only
      the database you created.
- [ ] **5. Root-cause every failure** down the layers this repo has (REFERENCE.md "Tracing"), to file and line, with an
      honest confidence: `Confirmed` with evidence, or `Probable` with what would confirm it. Never invent a cause.
- [ ] **6. Write `results.json`** (REFERENCE.md "results.json"), then generate the report; the script derives every count:

      ```powershell
      powershell -NoProfile -ExecutionPolicy Bypass -File .claude/skills/usecase-testing/scripts/Write-UseCaseReport.ps1 -ResultsPath "test-reports/$uc/results.json" -Format Xlsx
      powershell -NoProfile -ExecutionPolicy Bypass -File .claude/skills/usecase-testing/scripts/Write-UseCaseReport.ps1 -ResultsPath "test-reports/$uc/results.json" -Format Markdown
      ```

      A validation error names every problem: fix `results.json` and run it again, never hand-write the report.
- [ ] **7. Report back:** the workbook and version tested, verdict, pass/fail/blocked counts, defects by severity, the
      report paths, one line per issue (`severity | confidence | file:line | cause`), open spec questions separately,
      what was blocked and why, every test-code change. Offer the `prd-and-plan` skill on the Markdown report.

## Never

- Write a password, token, connection-string secret or personal data into `results.json`, the report or evidence.
  Accounts come from the developer's gitignored local settings or are created in the throwaway database; name them
  by role and permission keys only.
- Mark anything `Pass` that did not run; an unreachable service or database makes its tests `Blocked`.
- Skip the report when the run goes badly: a report of blocked tests with reasons is a valid deliverable.
