# P9 evidence: planning skills (prd-to-plan, prd-and-plan, reduce-comments)

Date: 2026-11-07. Task owner: planning-skills. Nothing committed or pushed.

## Delivered

| Path | What |
| --- | --- |
| `.claude/skills/prd-to-plan/SKILL.md`, `PLAN-TEMPLATE.md` | Turns an existing PRD into a tracked `plans/<slug>.md` of tracer-bullet phases. Each phase names its feature spec and the starter skills it runs |
| `.claude/skills/prd-and-plan/SKILL.md`, `PRD-TEMPLATE.md` | Writes the PRD (`plans/<slug>-prd.md`) and the plan in one run. The tracker is optional and set in `plans/README.md`; nothing about a tracker is hard-coded |
| `.claude/skills/reduce-comments/SKILL.md`, `REFERENCE.md` | Comments-only cleanup of one class or file, checked by the differ |
| `.claude/skills/reduce-comments/scripts/comment-diff.mjs` + `lib/` + `tests/` + `MODULE.md` | Comment counter and comments-only differ for C#, TS/JS, HTML templates and T-SQL. Zero-dependency Node ESM |
| `plans/README.md` | Plans are tracked in git; how they relate to `docs/features/`; the issue-tracker setting (`Tracker: none`) |
| `provenance/origins/p9-planning-skills.json` | 5 origins, all `rewritten` |

## Harvest

- Batch `p9-planning-skills` was added to a scratch copy of the manifest (`%TEMP%\p9-planning-skills`). Ran
  `tools\extract\extract.ps1 -Batch p9-planning-skills -Manifest <scratch> -Staging <scratch>\staging -Clean`.
  Copy: PASS, 5 files. Scrub: 45 findings, which is expected before a rewrite. Secret scan: clean.
- Every staged file was used only as a reference and rewritten. The source tracker server, agent and credential
  handling were left out, along with the source roles, workflow and comment markers. The Python verifier was ported
  to Node and redesigned.

## Verification

| Check | Result |
| --- | --- |
| `node --test ".claude/skills/reduce-comments/scripts/tests/**/*.test.mjs"` | 43 tests, 43 pass |
| Self-consistency sweep over every tracked cs/ts/js/mjs/html/sql file (3169) | Stripping every comment gives PASS. Changing one character inside the first string literal gives FAIL. 3169/3169 |
| Independent syntax check: comments stripped from 352 tracked JS/MJS files, then `node --check` | 0 broken: no string, template or regex was mistaken for a comment |
| `node tools/extract/check-origins.mjs --root .` | 0 findings |
| `node tools/facts-drift/check.mjs --root .` | 0 findings in the owned paths |
| Scrub sweep and secret scan, run on an isolated copy of the owned paths (31 files) | Scrub clean; secret scan clean (609 digests checked) |

A full-repo scrub sweep did not finish within 25 minutes because several parallel tasks were loading the machine,
so it was stopped. The integrator's full scan covers the whole repo.

## Differ design

- Each language has its own scanner. Segments (code, string literal, comment) tile the whole file.
- Comments are removed and replaced by whitespace: a line break if the comment spanned lines, otherwise a space.
- Runs of whitespace in code collapse to one space, or to one line break if the run held a line break.
- String literals are compared byte for byte.
- A failure reports the line of the first difference in each file.
- The scanners fail safe: when unsure, they treat text as code. The one known exception, a regex literal directly
  after `)`, is documented in `REFERENCE.md` and pinned by a test.
