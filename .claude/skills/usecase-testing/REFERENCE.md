# usecase-testing reference

Depth for [SKILL.md](SKILL.md). Read the section a step points to; nothing here overrides the repo itself.

## Resolving the input

- The user gives a use-case ID; normalise it to the form the workbook uses (`UC016`, `UC-016`).
- The workbook path comes from the user. Without one, list candidates (`git ls-files "*.xlsx"` and any folder the user
  names) whose file name or `Summary` sheet carries the ID, and ask. Several versions of the same use case: ask which;
  never pick the newest-looking one, and never test current code against a superseded workbook without saying so.
- No workbook for the ID: stop and say where you looked. Do not build a specification from the code.

## Workbook shape (HLS format)

An HLS workbook has one sheet per concern. Names and columns drift between generations (`Summary` or
`Scenarios Summary`, `Scenario ID` or `Scenario_ID`), so match by header text and log the mapping you used.

| Sheet (typical name) | What to take from it |
| --- | --- |
| `Summary` | Use-case ID, English and Arabic names, source document section and version, counts, and any key-facts block (actor, entry point, terminal status, transitions, notifications, blocking gaps). |
| `Smoke`, `Functional` | One row per scenario: ID, names, flow reference, priority, actor, preconditions, trigger, test type (positive or negative), linked rule, expected outcome (en/ar), notes with gap tags. |
| `Data` | Field, data type, mandatory, min, max, validation rule, valid and invalid examples, expected error (en/ar): the boundary and validation oracle. |
| `UI` | Element, expected text (en/ar), location, verification method: assert these strings verbatim. |
| `BusinessRules` | Rule ID, name, description, applies to, condition, expected behaviour, testable, scenarios, source lines. Each row becomes a Requirements row. |
| `StateTransitions` | Status, external projection, transition, trigger, actor: the oracle for every status assertion. Cross-check `docs/generated/statuses.md` and `docs/generated/workflows/`. |
| `Gaps_Messages` | Gap IDs with severity and the question to ask, message IDs: gaps are open spec questions, never defects. |

Dates may arrive as Excel serial numbers (days since 1899-12-30): convert before quoting. Rule tags in code comments
are what the code intended; a tag that contradicts the workbook is itself a finding.

## Test dimensions

Each dimension gets test cases or one `Blocked` row with the reason.

| Dimension | What to prove in this starter |
| --- | --- |
| Happy path | The main flow reaches the workbook's terminal status with the expected side effects. |
| Negative | Alternative and exception flows refuse with the right problem+json error code (`docs/generated/error-codes.md`). |
| Validation | Every `Data` rule: mandatory, format, cross-field; the validator's message key matches the expected error in ar and en. |
| Boundary | Min, max, min-1, max+1, empty, whitespace, longest Arabic text, dates at the deadline edge (use `FakeTimeProvider`, never the clock). |
| Authorization | 401 anonymous; 403 without the endpoint's permission key (`docs/generated/permissions.md`); grant scope (Global or own organization) hides other organizations' records; routes and buttons hidden without the key. |
| API contract | Route, method, PublicId (never an internal id) in routes and bodies, status codes, problem+json shape (`docs/generated/endpoints.md`, `docs/generated/openapi.v1.json`). |
| Database | The rows the flow must write (record, history, audit, outbox) and must not write; enums stored as integers. |
| Workflow | Only catalogued transitions, each with its permission and reason rule; illegal transitions refused (`Starter.Workflow.Testing`). |
| Deadlines and jobs | SLA timers, escalation and background jobs, driven by a fake clock or the job's own trigger. |
| Notifications | In-app items, e-mail (file drop) and SMS (file sink) per the workbook's message IDs, in the recipient's language. |
| UI | Screens per the `UI` sheet in ar (RTL) and en, exact strings, enabled and disabled actions per status and permission. |

## Starter test map

Run from the repo root with the private SDK (see the root `CLAUDE.md` Build section) and an alternate artifacts path,
so a running stack's DLLs are never overwritten:

```powershell
$dotnet = 'E:\codelab-tools\dotnet10\dotnet.exe'   # or $env:CODELAB_DOTNET
& $dotnet test backend/tests/Modules/Starter.Modules.<Module>.Tests --artifacts-path "$env:TEMP\uc-<id>" --filter "FullyQualifiedName~<Name>"
& $dotnet test backend/tests/Starter.Contract.Tests --artifacts-path "$env:TEMP\uc-<id>"
$env:CODELAB_TEST_SQL = '(localdb)\<TestInstance>'   # throwaway StarterIt_<guid> databases, created and dropped by the fixture
& $dotnet test backend/tests/Starter.Integration.Tests --artifacts-path "$env:TEMP\uc-<id>" --filter "FullyQualifiedName~<Name>"
npm test --workspace frontend/starter-web          # Vitest; put E:\codelab-tools\node24 first on PATH
```

| Layer | Where | Notes |
| --- | --- | --- |
| Use cases, validators, transitions, domain rules | `backend/tests/Modules/Starter.Modules.<Name>.Tests` | Fakes and `FakeTimeProvider`, no database. |
| Workflow transitions | `backend/tests/Starter.Workflow.Testing` (`TransitionFixture`) | Fluent assertions over a transition catalog through the real engine (`For`, `From`, `WithPermissions`, `Do`, `ExpectStatus`, `ExpectRefused`). |
| Endpoint contract | `backend/tests/Starter.Contract.Tests` | Permission or allowlist, PublicIds, validators, problem+json. |
| Real host and SQL | `backend/tests/Starter.Integration.Tests` | Throwaway database per fixture; [README](../../../backend/tests/Starter.Integration.Tests/README.md). |
| Module boundaries | `backend/tests/Starter.Architecture.Tests` | Fails when a fix crosses a module boundary. |
| Frontend services and components | `frontend/starter-web` (Vitest) | Specs sit next to the code. |
| Visual and accessibility | `npm run visual --workspace frontend/starter-web` | [e2e README](../../../frontend/starter-web/e2e/README.md); point `--base-url` at the slot web port. |
| Live UI | Playwright MCP browser tools | Drive the slot web app; screenshots under `test-reports/<UC-ID>/`. |

## Live stack on a slot

Never use, stop or reconfigure a stack someone else started (run-local refuses a second default stack) or its
database. Start your own slot with its own throwaway database on a named instance:

```powershell
sqllocaldb create <TestInstance> -s                      # once; a named instance only you use
$env:ConnectionStrings__Starter = 'Server=(localdb)\<TestInstance>;Database=StarterUc<Id>;Integrated Security=true;TrustServerCertificate=true'
powershell -NoProfile -ExecutionPolicy Bypass -File run-local.ps1 -Slot 1   # ports +100: API 5280, attachments 5285, web 4380; database StarterUc<Id>_Slot1
# ... tests ...
powershell -NoProfile -ExecutionPolicy Bypass -File run-local.ps1 -Stop -Slot 1
```

Then drop the database you created (and only it). Slot details: [tools/run-local/README.md](../../../tools/run-local/README.md).
Accounts: the developer's gitignored local settings (`Identity:DevUsers`, see
[tools/devseed/README.md](../../../tools/devseed/README.md)) or users created through the admin API in your
throwaway database. Reference them by role and permission keys; never copy a password into any output. A probe that
changes data runs only against your throwaway database; say so in the evidence.

## Tracing

Follow the layers the repo has, as in [CONTEXT.md](../../../CONTEXT.md) "Use-case tracing":

```
route and component -> ApiService client -> controller ([HasPermission]) -> validator -> use case (Outcome<T> or Refusal)
  -> domain and workflow transition -> port -> repository / DbContext -> SQL schema of the owning module
```

Background work adds: job or outbox message -> handler -> port. Stop at the line that produces the wrong behaviour,
and record entry point, `file:line`, call chain and database object.

| Confidence | Meaning |
| --- | --- |
| `Confirmed` | Evidence proves it: a test flips when that line is exercised or changed, or a stack trace or log names the frame. |
| `Probable` | Consistent with the evidence but unproven. Give `confirmWith`: the evidence that would confirm it. |

| Severity | Use for |
| --- | --- |
| `Critical` | Security or authorization bypass, data loss or corruption, or the main flow blocked with no workaround. |
| `High` | A business rule or status transition wrong in the main flow; wrong data persisted. |
| `Medium` | An alternative or exception flow wrong, a wrong or missing validation, a workaround exists. |
| `Low` | Wording, layout, a missing translation with no behavioural impact. |

## results.json

UTF-8 JSON read by `scripts/Write-UseCaseReport.ps1`. Required fields are marked; the script refuses invalid input and
lists every problem.

| Path | Content |
| --- | --- |
| `useCase.id`* `.name` `.workbook`* `.workbookVersion` | The use case and the workbook tested against. |
| `run.testedOn` `.branch` `.commit` `.environment[]` | Run facts; `environment` lists stack, test projects, slot and instance (no secrets). |
| `executiveSummary` | Three to six sentences: what works, what is broken, the biggest risk. |
| `testCases[]` | `id`* (the workbook ID verbatim, or `TC-<UC>-NNN` with the reason in `title`), `title`, `dimension`, `workbookRef` (sheet and row), `expected`*, `actual`, `status`* (`Pass`, `Fail`, `Blocked`, `Skipped`), `evidenceIds[]`, `verifiedBy`, `unblock`. |
| `issues[]` | `id`*, `title`, `severity`*, `confidence`*, `testCaseIds[]` (must exist), `ruleIds[]`, `permissions[]`, `layer`, `problem` (the user's words), `expected`, `actual`, `reproSteps[]`, `rootCause`, `location` (`file:line`, required when Confirmed), `callChain`, `confirmWith` (required when Probable), `recommendedFix`, `acceptanceCriteria[]`, `blastRadius`. |
| `evidence[]` | `id`, `type` (`Environment`, `Command`, `Http`, `Sql`, `Log`, `Screenshot`), `description`, `command`, `exitCode`, `location`. |
| `requirements[]` | `id`*, `rule`, `testable` (`Yes` or `No`), `testCaseIds[]`, `result`, `note`. A testable rule needs a test case or `result: Blocked`. |
| `openQuestions[]` | `id`, `gap`, `scenarioIds[]`, `specSays`, `appDoes`, `rulingFrom`. |
| `fixOrder[]` | Ranked issue IDs with a one-line reason; group issues that share a root cause. |
| `testCodeChanges[]` | Every test file added or changed; production code stays untouched. |

The verdict is derived: `FAIL` with any failed test or issue, `INCOMPLETE` with blocked tests or no tests, else `PASS`.
`-Format Xlsx` writes `<UC-ID>-test-report.xlsx` (Summary, Test Cases, Issues, Evidence, Requirements, Open Questions);
`-Format Markdown` writes `<UC-ID>-test-findings.md`; `-OutputPath` overrides either.

## Read-Xlsx.ps1

`-Path` (required), `-Sheet <name>` (case-insensitive; an unknown name fails and lists the sheets), `-AsJson`,
`-OutFile <file>` (UTF-8 without BOM: use it whenever another process reads the output, so Arabic survives the
console code page), `-MaxCellChars` (default 4000). Text: `===== SHEET: <name> (<part>) =====` then
`[r<row>] a | b | c` (newlines become ` / `, pipes `/`). JSON: `[{ "name", "part", "rows": [{ "row", "cells": [] }] }]`,
cells raw. Missing and empty cells are `''` at their column; rows without text are skipped; booleans are `TRUE` or
`FALSE`. Windows PowerShell 5.1 `ConvertFrom-Json` returns the whole array as one object: return it from a function or
index into it before piping. Errors are terminating (exit code 1 under `-File`). Workbooks are untrusted: a part that
declares a DTD is refused with `Refused: ... workbook part '<part>'` (no entity is ever resolved or expanded, so no
local-file, UNC or HTTP fetch).

## Error handling

| Situation | Do this |
| --- | --- |
| No workbook, or several versions | Stop and ask; name what you found. |
| `Read-Xlsx.ps1` fails | Report the exact error; never infer sheet contents from the file name. A `Refused:` error means the workbook declares a DTD: report it as a security finding and ask for a clean copy; never open it another way. |
| Workbook contradicts another spec or a code tag | Open spec question; the workbook still drives the test. |
| Build fails | Finding number one: capture, trace, still write the report. |
| Service or database unreachable, or no free slot | Dependent tests `Blocked` with the reason. |
| Zero defects | Still write the report: passed, blocked and rule coverage are the deliverable. |

## Scripts and tests

`scripts/Read-Xlsx.ps1` and `scripts/Write-UseCaseReport.ps1` run on Windows PowerShell 5.1 with no
Excel, Python or module (System.IO.Compression). Pester 3.4 tests build their workbooks and results files in `%TEMP%`
at test time:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester .claude/skills/usecase-testing/scripts/tests -EnableExit"
```
