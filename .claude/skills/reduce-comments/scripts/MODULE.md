# reduce-comments scripts

## Purpose

`comment-diff.mjs` is the proof behind the `reduce-comments` skill: it counts comment units and verifies that an
edit changed comments only. Zero dependencies, Node 24, ES modules.

## Use

```powershell
node .claude/skills/reduce-comments/scripts/comment-diff.mjs count <file> [--json]
node .claude/skills/reduce-comments/scripts/comment-diff.mjs verify <baseline> <edited> [--json] [--dialect csharp]
node --test ".claude/skills/reduce-comments/scripts/tests/**/*.test.mjs"
```

Exit codes: 0 comments only (or counted), 1 code changed, 2 usage, unreadable or unsupported file.

| File | Role |
| --- | --- |
| `lib/scan-csharp.mjs`, `lib/scan-script.mjs`, `lib/scan-html.mjs`, `lib/scan-sql.mjs` | One scanner per language: split the text into code, string-literal and comment segments that tile the file |
| `lib/segments.mjs`, `lib/text-scan.mjs`, `lib/c-style-comments.mjs` | Segment list and shared character helpers |
| `lib/dialects.mjs` | File extension to dialect and scanner |
| `lib/normalize.mjs` | Drops comments, collapses code whitespace, keeps literals verbatim, remembers source lines |
| `lib/verify.mjs`, `lib/count.mjs` | The comments-only comparison and the comment-unit count |
| `lib/cli.mjs`, `lib/source-file.mjs` | Arguments, output, exit codes; UTF-8 and UTF-16 (BOM) reading |

## Extend

- **A new language:** add `lib/scan-<name>.mjs` returning `SegmentList.finish()`, register it in `Dialect`,
  `SCANNERS` and `EXTENSIONS` (`lib/dialects.mjs`), and add tests: strings holding the comment openers, every
  literal form, nested comments if the language has them, and a verify test showing a string edit fails.
- **A new literal form in an existing language:** extend that scanner's literal check and add a scanner test plus a
  verify test.

## Rules

- When unsure, classify as code or literal, never as comment: a misclassified comment is a false alarm, a
  misclassified piece of code could let a code change pass.
- Literals are compared byte for byte; only code whitespace is normalized, and a line break never collapses into a
  space.
- No dependencies; tests use `node:test` only.

## Gotchas

- The script scanner decides regex versus division from the previous significant token; a regex right after `)` is
  read as division (pinned by a test, documented in the skill's REFERENCE.md).
- TSX and JSX are deliberately unsupported.
- Interpolation holes (C# `{}`, template `${}`) are scanned to find the end of the literal, but the whole literal,
  holes included, is compared verbatim.
