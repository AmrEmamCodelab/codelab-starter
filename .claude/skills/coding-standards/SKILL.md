---
name: coding-standards
description: The codelab-starter coding standards (named arguments, lookup constants, enums as integers, no magic strings or numbers, mandatory unit tests, error handling, thin controllers and components, module boundaries) that every implementation and every review must follow. Use whenever implementing anything (feature, bug fix, refactor, new endpoint, screen or use case), whenever conducting any code, branch, PR or diff review, and whenever coding standards, conventions, best practices or error handling are mentioned. Load before the first code edit or the first review finding.
---

# Coding standards

The standards live in `docs/standards/` so people and agents read one copy. This skill only loads them.

1. Read [docs/standards/SKILL.md](../../../docs/standards/SKILL.md) in full before the first edit or the first review finding.
2. Before writing or reviewing a `throw`, `catch`, refusal or log call, read [docs/standards/ERROR-HANDLING.md](../../../docs/standards/ERROR-HANDLING.md).
3. Before applying an unfamiliar standard, read its row in [docs/standards/EXAMPLES.md](../../../docs/standards/EXAMPLES.md).
4. Also read the `MODULE.md` and nested `CLAUDE.md` of every module you touch; module rules add to the standards.

Standards 13-18 are enforced by `tools/build.ps1 -Target CI` (banned APIs, analyzers, rules, tests); the rest are enforced in review.
