# Architecture decision records

| ADR | Title | Status |
| --- | --- | --- |
| [ADR-000](ADR-000-hybrid-strategy.md) | Hybrid strategy | Accepted |
| [ADR-001](ADR-001-fresh-history-extraction.md) | Fresh history, extraction manifest and harvest copy | Accepted |
| [ADR-002](ADR-002-package-consumption.md) | Package consumption | Accepted |
| [ADR-003](ADR-003-full-rename-upstream-merge.md) | Full rename and rename-replay upstream merge | Accepted |
| [ADR-004](ADR-004-module-structure.md) | Module structure | Accepted |
| [ADR-005](ADR-005-node-pin.md) | Node pin | Accepted |
| [ADR-006](ADR-006-hook-architecture.md) | Hook architecture | Accepted |
| [ADR-007](ADR-007-repo-host-ci.md) | Repository host and CI | Accepted |
| [ADR-008](ADR-008-handover-kit.md) | Handover kit (full dependency closure, offline npm cache) | Accepted (2026-11-04; v0 script in phase 1, final kit in phase 10) |
| [ADR-011](ADR-011-architecture-test-library.md) | Architecture-test library | Accepted (conditional on the spike) |
| [ADR-012](ADR-012-clean-code-enforcement-phasing.md) | Clean-code size and magic-number enforcement phasing | Accepted (2026-11-04; C# analyzers landed in phase 1) |

Later phases write ADR-009 (font and icon licences) and ADR-010 (migration squash).

Each ADR has a Status, Date and Deciders line, followed by Context, Decision and Consequences sections. A changed decision gets a new ADR that supersedes the old one; accepted ADRs are not rewritten. This folder is excepted from the MODHS term sweep because ADRs record where the starter came from.
