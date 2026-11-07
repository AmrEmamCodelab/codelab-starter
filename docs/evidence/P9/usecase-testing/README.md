# P9 evidence: usecase-testing skill

The harvested use-case testing skill and the test-use-case command are merged into one skill,
`.claude/skills/usecase-testing/` (commands become skills), with a dependency-free xlsx reader and a report writer.

## What was delivered

| File | Purpose |
| --- | --- |
| `.claude/skills/usecase-testing/SKILL.md` | The workflow: input = use-case ID + any HLS-format workbook path; inspect, read, plan, run existing tests, live checks on a `run-local.ps1 -Slot` stack with its own throwaway database, root cause, report. |
| `.claude/skills/usecase-testing/REFERENCE.md` | Workbook shape, test dimensions, starter test map, slot stack, tracing, severity and confidence rubrics, the `results.json` contract, the reader contract, error handling. |
| `.claude/skills/usecase-testing/scripts/Read-Xlsx.ps1` | `-Path`, `-Sheet`, `-AsJson`, `-OutFile`, `-MaxCellChars`; System.IO.Compression OOXML read on Windows PowerShell 5.1, no Excel. |
| `.claude/skills/usecase-testing/scripts/Write-UseCaseReport.ps1` | `results.json` -> `.xlsx` (Summary, Test Cases, Issues, Evidence, Requirements, Open Questions) or a Markdown bug report for `prd-and-plan`; validates input, derives counts and the verdict. |
| `.claude/skills/usecase-testing/scripts/tests/` | Pester 3.4 tests; `XlsxFixture.ps1` builds the workbook in `%TEMP%` at test time. |
| `provenance/origins/p9-usecase-testing.json` | Origins of the three harvested files, `promotion: rewritten`. |

## Harvest

Batch `p9-usecase-testing` (three entries, mode copy, targets under `reference/p9/usecase-testing/`) was added to a
scratch copy of the allowlist and extracted with
`tools\extract\extract.ps1 -Batch p9-usecase-testing -Manifest <scratch manifest> -Clean`: 3 files staged, 0
hard-denied. The staged command carried source-product role, project and service names (20 scrub findings) and one
known-secret-digest match (a seeded-account password); none of that text was carried into the rewrite. The batch
YAML goes to the integrator as a shared-file request.

## Checks (2026-11-07)

| Check | Command | Result |
| --- | --- | --- |
| Pester 3.4 (Windows PowerShell 5.1) | `powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester .claude/skills/usecase-testing/scripts/tests -EnableExit"` | 30 passed, 0 failed (Read-Xlsx 14, Write-UseCaseReport 16) |
| Provenance origins | `node tools/extract/check-origins.mjs --root .` | 47 files, 0 findings |
| Scrub sweep | `node tools/extract/scrub-sweep.mjs --root .` | 0 findings in this task's paths (49 findings, all in `rules/rules.compiled.json`, owned by another task) |
| Secret scan | `node tools/extract/secret-scan.mjs --root .` | 0 findings in this task's paths (1 finding in `tools/ai-evals/tests/HeadlessClaude.Tests.ps1`, owned by another task) |

What the reader tests prove: shared strings (plain, `xml:space`, rich-text runs, phonetic runs skipped), inline
strings with several runs, numbers, booleans, missing and explicitly empty cells as `''` at their column, a cell
without a reference, empty rows skipped, relative and absolute relationship targets, `-Sheet` (case-insensitive,
unknown name fails and lists sheets, non-zero exit under `-File`), `-OutFile` UTF-8 without BOM with Arabic text,
truncation, missing file and non-zip errors.

What the writer tests prove: the six worksheets in order, derived counts and verdicts (FAIL, INCOMPLETE, PASS),
Arabic and XML markup round-trip, Markdown sections and issue fields, and refusal of an unknown status, a Confirmed
issue without `file:line`, a Probable issue without `confirmWith`, an uncovered testable rule, a report without a
workbook and a missing results file.

## Not done here

- No live run of the skill against a real workbook (headless budget for this task is 0); the Permit pipeline proof
  exercises it with an HLS-format workbook.
- The CLAUDE.md routing row, the allowlist batch and the `test-reports/` ignore entry are shared files: requested
  from the integrator.
