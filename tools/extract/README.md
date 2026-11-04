# tools/extract/

## Purpose

Keeps the starter free of source-product code, names and secrets (ADR-001). It holds:

- **The allowlisted extraction.** `allowlist.manifest.yaml`, `extract.ps1` and `extract-core.mjs` are the only way a
  harvested file enters the repo. Hard-deny always wins over the allowlist.
- **The scrub sweep.** `scrub-sweep.mjs` with `modhs-terms.txt` and `scrub-exceptions.txt` covers source-product
  terms (English and Arabic, also inside identifiers and file names), brands, organisation names, IPv4 and IPv6
  addresses, .sa and internal hosts, UNC paths, SQL Server instances, e-mail addresses, Saudi mobile numbers,
  national-ID-like numbers, LDAP domains and the DGA package check. It is CI step 1.
- **The secret scan.** `secret-scan.mjs` runs regex rules plus `secret-digests.json`, a SHA-256 denylist of known
  source-product key material. It is CI step 2, and `build.ps1` adds gitleaks when it is installed.
  `secret-exceptions.txt` lets named regex rules match only where the TEST-ONLY fixture keys and golden vectors live
  (`packages/vectors`); the digest denylist is never excepted.
- **What is scanned.** Both scanners read UTF-8 and UTF-16 (with or without a BOM) text and open zip containers
  (docx, xlsx, pptx, nupkg, zip): each entry is scanned as `file.docx!word/document.xml`, with OOXML runs joined.
  Any other binary is a finding (`unscannable-binary`) unless it is an image or font (`lib/files.mjs`).
- **Provenance.** `generate-source.mjs` writes `provenance/SOURCE.md`. It records the source commit and tree hash, the
  harvest manifest check, the per-file origin map and the hard-deny list.

## Use

```powershell
# CI steps 1 and 2 (exit 0 clean, 1 findings, 2 usage/internal error); --json for machine output
node tools/extract/scrub-sweep.mjs --root .
node tools/extract/secret-scan.mjs --root .

# Tests (CI step 10)
node --test "tools/extract/tests/**/*.test.mjs"

# Extract a batch into %TEMP%\codelab-extract\<batch> and scan it (exit 1 = not promotable)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\extract\extract.ps1 -Batch p0 -Clean

# Regenerate provenance/SOURCE.md (exit 1 = written, but the harvest check is not a 100% byte match)
node tools/extract/generate-source.mjs

# Rebuild the digest denylist from the harvest copy (platform owner, once per harvest commit)
node tools/extract/build-secret-digests.mjs
```

**Promote a batch.** Nothing is promoted automatically.

1. Run `extract.ps1` and read its findings. Staged files keep source-product names until you rewrite them.
2. Rewrite the staged files with starter names until both scans pass.
3. Copy them to their target paths in the repo.
4. Copy `<staging>\origins.json` to `provenance\origins\<batch>.json`.
5. Run `node tools/extract/generate-source.mjs` and commit the result.

`origins.json` holds one entry per file: batch, entry id, source and target paths, mode, the source git blob, and
the source and staged SHA-256. When you promote, set `target` to the repo file the rewrite produced, keep the staging
path as `stagedTarget`, and set `promotion: rewritten`. A batch that is extracted and scanned but not promoted yet is
recorded with `promotion: staged`. An entry deliberately not carried (left for a later phase, or not a package
primitive) also stays `staged` and must carry a `note` or `deferred` field giving the reason. The package spike is
recorded as three batches (`pkg-spike-security`, `pkg-spike-observability`, `pkg-spike-sample`) sharing one source.

## Extend

- **New harvest batch.** Add `batches.<name>.entries` (`id`, `source` glob, `target`, `mode: copy | headings-only`)
  to the manifest. A glob source maps everything below its literal prefix under `target/`. An entry that matches
  nothing makes `extract-core.mjs` exit 1.
- **A batch from another source** (not the harvest commit, e.g. the package spike): give the batch
  `source: { description, harvestRoot, contentDigest }`. Make a durable read-only copy first (never read from
  `%TEMP%`), pin it with `contentDigest` (`lib/hashing.mjs`); `extract-core.mjs` refuses to copy when the folder
  no longer matches.
- **New term.** Add `<id>[/i]: <regex>` to `modhs-terms.txt`, then add a flagged sample to
  `tests/fixtures/scrub-samples.json`. Rule ids must not spell the term itself, because ids appear in code and
  test files.
- **New built-in scrub rule.** Add it to `lib/scrub-rules.mjs` (`id`, `pattern` with `g`, `scope`, optional
  `appliesTo`, `isAllowedMatch` or `mask`) and add samples.
- **New secret rule.** Add it to `lib/secret-rules.mjs`. Tests must assemble secret-shaped input at run time with
  `joined(...)` so the repo itself stays clean.
- **New exception.** Add a line to `scrub-exceptions.txt` (`<glob> <rule ids>`). Every exception is a reviewed
  decision.
- **New secret exception.** Add a line to `secret-exceptions.txt` (same format, stricter: exact rule ids only, a glob
  anchored to a folder, never `known-secret-digest` or `unscannable-binary`; see `lib/secret-exceptions.mjs`), and
  add a test to `tests/secret-exceptions.test.mjs` proving the same content elsewhere is still a finding.

## Rules

- Never write to the source repository or the harvest copy. Only read-only git is allowed (`rev-parse`, `ls-tree`,
  `hash-object` without `-w`).
- Source-product terms are allowed only under `provenance/` and `docs/adr/`. The national-integration terms are also
  allowed in `docs/integrations/gateway-extension-points.md`. The sweep's own configuration files and
  `tests/fixtures/` are the only other exceptions.
- `secret-digests.json` stores digests and a coarse kind only. No value, no path and no setting name is stored.
- Scanners never print a secret. Excerpts are redacted (`abcd****`) and national IDs are masked.
- Hard-denied paths are never harvested, whatever the allowlist says: memory, handoffs, the deploy-kit reference,
  deploy config JSON, `check-env-*`, the national-integration services and files, the external DGA tarball, key
  files and `node_modules`.

## Gotchas

- **The harvest is not a byte-exact match today.** 125 files under `services/attachment-service` came out of
  `git archive` with CRLF line endings. That folder has a nested `.gitattributes` with `text=auto`, and `core.eol`
  defaults to native on Windows. The content is identical (checked by LF normalisation). Re-create the copy with
  `-c core.eol=lf` to reach 100%; `provenance/SOURCE.md` has the details.
- **Scope of the sweeps.** Inside a git work tree the sweeps scan tracked and untracked, not-ignored files, so
  `.gitignore` is honoured. Outside one, such as a staging folder, they scan EVERY file except `.git`: a nested
  `dist/`, `artifacts/` or `bin/` in a batch is scanned, never promoted unseen.
- **Matching rules.** Term rules match the line as written and a normalized copy: identifiers split on case, digit
  and `_` boundaries (PascalCase, camelCase, SCREAMING_CASE), Arabic without tashkeel/tatweel and with alef forms
  unified (`lib/term-text.mjs`). Write Arabic terms in that normalized form. Term rules also match file names. The DGA check applies only to package manifests, lockfiles,
  `angular.json` and code or style files.
- **Allowed by design.** `in-flight` is not an aviation term. `appsettings.local.json` is a file name, not a host.
  Four dotted numbers count as a version only directly after a version keyword (`<AssemblyVersion>1.0.0.0`,
  `"version": "1.2.3.4"`, `Version=4.0.0.0`); "version" elsewhere on the line exempts nothing. Reserved test
  domains (`example.test`, `*.invalid`, `example.com`) are allowed in e-mail addresses and UNC hosts.
- **Secret-scan heuristics.**
  - A secret-named setting whose value is a lowercase name containing a secret word (`config-secret`) is treated
    as a name; any other value, including a lowercase passphrase, is a finding.
  - Unquoted `NAME=value` / `name: value` secrets are checked in env, YAML, PowerShell, shell, ini and toml files.
  - The high-entropy fallback ignores hex (hashes) and `sha*-` integrity strings.
  - `"//Name"` documentation keys are skipped.
- **Digest denylist.** Each value is hashed as text and as the key bytes it decodes to (hex in any case, base64,
  base64url; `lib/key-material.mjs`), and the scanner also tries adjacent string literals joined, so a re-encoded or
  split key still matches. The SHA-256 of a public input (the empty body, `{}`) is never reported, although the
  harvest listed it as a fixture: `lib/public-digests.mjs` holds those preimages, never values. Sources include compose files, CI workflows, scripts, SQL, launchSettings and environment
  files (`secretDigests.scriptFiles`). The denylist file is not matched against itself.
