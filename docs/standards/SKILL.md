# Coding standards

**Always on.** Apply these standards to every implementation and every review in this repository, together with any other skill or agent in use (`do-work`, the scaffold skills, reviewers). They define what correct code looks like here: implement to them, and review against them. The `coding-standards` skill in `.claude/skills/` loads this file.

Scope: C# under `backend/`, `services/` and `packages/dotnet/`, and TypeScript under `frontend/` and `packages/npm/`. Standards 13-18 (the clean-code base rules) are also encoded in `rules/base.rules.yaml`, which every module inherits; the build and CI enforce what the rules engine can check, and review covers the rest.

> Type names used below (`Starter.Domain`, `Codelab.Errors`, `GlobalExceptionHandler`, `CodedRefusal`, the sample `ServiceRequest` module) are the target contract of the build plan. Most arrive in phases 1, 2 and 7. Verify a name in code before copying it.

## Before editing

1. Read the root `CONTEXT.md`, the `MODULE.md` and nested `CLAUDE.md` of the module you are changing, the relevant code, its callers, contracts and tests. Identify the component that owns the behaviour.
2. State the intended outcome and the existing behaviour that must stay valid, including edge cases, failure behaviour and compatibility requirements.
3. For a bug, reproduce the failure with a focused test or a minimal reproducer when feasible. If reproduction is blocked, state the evidence and the uncertainty; never present a hypothesis as a confirmed cause.
4. Choose the smallest coherent change. When adding an abstraction or choosing between substantially different fixes, compare two approaches briefly before selecting one.
5. Choose verification that checks observable behaviour. Reuse the existing test commands. Add a regression test for a reproducible bug; avoid tests that merely repeat the implementation.

Before the first edit, give a short update: the intended change, the constraints to preserve and the verification plan. Scale it to the task. Proceed without an approval checkpoint when the work is already authorized; ask only when missing information materially affects correctness or scope.

## Standards while coding

### Design (1-12)

1. **Name same-type arguments.** For a new or changed signature with two or more parameters of the same type, make swapped values hard to write. Prefer, in order: distinct ID types where the boundary already has them (`PublicId<T>` at the API), a request or options record with named properties, or C# named arguments at the call site. Do not rewrite existing ports only for this rule.
2. **Give each rule one owner.** Keep a business rule, invariant or representation in the component responsible for it. Consolidate duplicated knowledge; do not merge unrelated rules just because their code looks alike.
3. **Hide implementation details.** Expose the operations callers need. Keep storage formats, internal state and mechanics private, and document the guarantees callers rely on.
4. **Make modules earn their interfaces.** Add a class, layer or helper when it hides meaningful complexity or forms a useful boundary. Avoid forwarding-only wrappers, but keep the kernel layers and Domain ports described in `CONTEXT.md` even when they mostly forward: they are required boundaries and make test fakes possible. Split by independently understandable responsibility, not by line count.
5. **Keep reusable mechanisms independent.** Keep screen-specific actions, caller-specific rules and feature policy out of shared infrastructure. Put specialization in the use case that owns it.
6. **Make common usage simple.** Provide sensible defaults and do shared setup inside the responsible module. Avoid hidden call sequences. Expose configuration only where the caller has a real decision to make.
7. **Use precise, consistent names.** Names reveal the concept or operation and follow the repository's terms. If a responsibility is hard to name, reconsider its boundary before reaching for `Manager`, `Helper` or `Process`.
8. **Make necessary contracts explicit.** Document units, bounds, null semantics, ownership, side effects, errors and ordering requirements. Comments explain intent, constraints or rationale that the code cannot; update them with the code.
9. **Simplify errors without hiding failures.** Use domain exceptions (`BusinessRuleException`, `AccessDeniedException`, `ResourceNotFoundException`) for broken invariants and missing resources, and a refusal result (`CodedRefusal`) for expected business outcomes. Wrap external dependency failures in `UpstreamUnavailableException`. Catch only specific, recoverable failures, or isolate best-effort side effects per item. Never swallow a failure to return success, never catch-log-rethrow, and let `OperationCanceledException` propagate. Read [ERROR-HANDLING.md](ERROR-HANDLING.md) before writing a `throw`, `catch`, controller refusal or log call.
10. **Keep the change coherent and bounded.** Follow established patterns and improve the affected abstraction when needed. Avoid speculative frameworks, unrelated cleanup and special cases that duplicate existing behaviour. Do not pick a tiny patch that leaves the real cause unfixed.
11. **Justify performance complexity.** Measure before and after an optimization; prefer removing work or a better algorithm. Add caching only with a freshness contract, preferring `IMemoryCache` or `HybridCache` over unbounded static dictionaries. Never claim a speed-up without evidence.
12. **Use named lookup values.** Compare and assign lookup IDs (reference data seeded by migrations, such as statuses) only through the module's constants class, for example `ContactTopicIds.General`, whose values mirror the IDs the migration writes. Never write a bare number such as `StatusId == 4`, never invent the meaning of an existing ID, never add an enum that mirrors seeded lookup IDs, and never compare translated display labels.

### Clean-code base rules (13-18, inherited by every module)

13. **Enums are stored as integers.** Every enum member has an explicit value (`Approved = 3`), so reordering members never changes stored data. EF stores enums as `int` (its default); `HasConversion<string>()` on an enum is banned. The API sends the numeric value, and the Angular client uses a generated TypeScript enum plus label keys; bilingual labels come from i18n, never from enum member names. Use an enum for a closed set the code owns (a job-run state, a notification channel); use lookup constants (standard 12) for reference data the database seeds.
14. **No magic strings or numbers.** Status and lookup IDs, role keys, permission keys, error codes, configuration keys, claim types, route names, cache keys, job IDs and limits live in typed constants or options classes. Allowed bare literals in non-test code: `0`, `1`, `-1`. String literals used as keys are refused: `[HasPermission("...")]`, `configuration["..."]`, `IsInRole("...")`, `router.navigate(['/...'])`. On the frontend, permission keys, routes and API paths come from typed constants as well.
15. **Small, single-purpose code.** One method does one thing at one level of abstraction. Use guard clauses instead of nested conditionals. Delete dead and commented-out code; version control remembers it. Method length, file length and cyclomatic complexity limits are analyzer errors, with thresholds per module in its rules file.
16. **No business logic in controllers or components.** A controller binds, authorizes (`[HasPermission]`), calls one Application use case and maps its result. An Angular component renders state and forwards user intent to a service. Rules live in Domain and Application (backend) or in services and stores (frontend).
17. **Time comes from `TimeProvider`.** `DateTime.Now`, `DateTime.UtcNow`, `DateTime.Today`, `DateTimeOffset.Now` and `DateTimeOffset.UtcNow` are banned APIs (RS0030 from Codelab.Build.Sdk). Inject `TimeProvider` so tests control the clock.
18. **Unit tests are mandatory.** Every Application service, validator, workflow transition, domain rule, and Angular service or component with logic ships with unit tests in the same change. Enforcement: a contract test requires a test class for every public Application use case; a coverage gate per module (default at least 80% line coverage on Domain and Application, 70% on frontend core and features); the scaffold skills always emit tests with the code; and the commit gate refuses logic without tests.

Before applying an unfamiliar standard, read its row in [EXAMPLES.md](EXAMPLES.md): a reference shape plus BAD/GOOD examples for standards 1, 2, 6, 12, 13 and 14. Error handling has its own [ERROR-HANDLING.md](ERROR-HANDLING.md).

## When reviewing

1. Establish the change set (diff, branch, PR or named files) and read enough surrounding code, callers and tests to judge it. Do not review lines in isolation.
2. Check every changed file against standards 1-18 and the red flags in [EXAMPLES.md](EXAMPLES.md). Run the reviewing steps in [ERROR-HANDLING.md](ERROR-HANDLING.md) for every changed `throw`, `catch`, controller refusal and log call. Check lookup comparisons, enum declarations, literals used as keys and same-type signatures explicitly.
3. Report only findings you can tie to a standard or a concrete failure: `file:line`, the standard broken, the failure scenario and the fix. Rank by severity. Do not flag pre-existing code the change did not touch unless the change depends on it.
4. When another skill or tool sets the review format, keep that format and cite the standard in each finding.

## Before finishing

- [ ] The requested behaviour works, including the relevant edge cases and failures.
- [ ] Required contracts are preserved, or authorized changes include the affected callers.
- [ ] New or changed same-type signatures name their arguments.
- [ ] Lookup comparisons use named constants; every enum member has an explicit integer value.
- [ ] No magic strings or numbers used as keys, IDs or limits.
- [ ] New logic has unit tests, and they ran green.
- [ ] Rules have clear owners; new abstractions reduce caller burden; architectural ports are intact.
- [ ] Names, comments and docs (`MODULE.md`, generated docs) match the final behaviour.
- [ ] `tools/build.ps1 -Target CI` (or the relevant subset) ran; unverified behaviour and remaining uncertainty are stated.
- [ ] Performance claims, if any, have measurements.

Report what changed, how it was verified and any material limitation. Do not claim a test passed unless it ran successfully.

## Judgment and scope

Apply repository requirements and the user's instructions. If a standard conflicts with a required contract, preserve the contract and say why. Treat design red flags as prompts to investigate, not automatic bans; the base rules (13-18) are the exception, because CI enforces them. These standards load together with diagnosis, testing, implementation and review workflows, never instead of them, and add no separate approval process.

Design principles 1-12 are adapted from John Ousterhout's *A Philosophy of Software Design*, second edition. Named arguments, lookup constants and the clean-code base rules are Codelab conventions.
