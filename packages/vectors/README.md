# packages/vectors

## Purpose

The golden vectors that pin the C# security core (`Codelab.Security`) and its TypeScript twin (`@codelab/ng-security`)
to the same bytes. Nothing in this folder is shared code: each runtime computes every value independently, and the
files are the contract between them.

| File | What it pins |
| --- | --- |
| `canonical.vectors.json` | `{Prefix}-CANON-v1` framing: empty lists and fields, boundary shifts, delimiters and NUL in fields, Arabic, Arabic-Indic digits, bidi marks, astral characters, unnormalised combining marks, a large field |
| `session-keys.vectors.json` | the HKDF info and the per-session signing key, for both master keys |
| `request-signature.vectors.json` | `sign`: body hash, canonical bytes, MAC and header (empty, JSON, Arabic, binary, large and exempted bodies; unsorted and repeated queries; path-escape normalisation). `verify`: one baseline request presented with one thing changed, and its verdict |
| `response-signature.vectors.json` | the same for response signatures, judged as the browser judges them (the exemption marker counts) |
| `public-id.vectors.json` | `protect`: AAD, both HKDF infos and the token (ids up to 2^63-1, as strings). `open`: a token and its outcome |
| `fixture-keys/*.json` | the TEST-ONLY keys every value above is computed with |

Every file is `{ schemaVersion, area, prefix, fixtureKeyId, notice, cases: [ { name, kind, <inputs>, expected… } ] }`.
Verdicts are `valid`, `malformed` (the header does not parse, or its MAC is not the one canonical spelling of 32
bytes) and `mismatch`.

## Use

```powershell
# Cross-language check (CI step 12): exit 0 only when C# and TypeScript agree
node tools/vectors/verify-vectors.mjs --prefix CODELABTEST
node tools/vectors/verify-vectors.mjs --prefix ALTPFX          # a set generated for another prefix

# Each half on its own
E:\codelab-tools\dotnet10\dotnet.exe test packages/dotnet/Codelab.Security.Tests/Codelab.Security.Tests.csproj --filter Category=GoldenVectors
npm test -w @codelab/ng-security
```

The C# runner is `packages/dotnet/Codelab.Security.Tests/Vectors` (through `Codelab.Testing`'s `GoldenVectorRunner`);
the TypeScript runner is `packages/npm/ng-security/src/vectors`. A forked project copies this folder with
`codelab-vectors-sync` from `@codelab/ng-tools`.

## Extend

- **A new case:** add it to the C# catalogue (`Vectors/*Cases.cs` or the area's `Inputs`), regenerate, review the diff
  and run both prefixes through `verify-vectors.mjs`. The TypeScript specs pick new cases up by kind.
- **Regenerate:** `$env:CODELAB_REGEN_VECTORS='1'; dotnet test ... --filter Category=GoldenVectors` (then clear the
  variable). A test fails while a checked-in file differs from what the generator writes.
- **A new area:** an `IVectorArea` in C#, a `*.vectors.spec.ts` in `ng-security/src/vectors`, and the file name in
  `tools/vectors/lib/verify-plan.mjs` `VECTOR_FILES`.
- **New fixture keys:** `node tools/vectors/fixture-keys.mjs generate --force`, then
  `node tools/vectors/fixture-keys.mjs published-material`, then regenerate every vector file.

## Rules

- **Generated, never edited by hand.** The expected values come from the C# implementation; the TypeScript runner is
  the independent check.
- **TEST-ONLY keys.** The fixture keys are fresh starter keys, never a harvest-source value (a test checks every digest
  against `tools/extract/secret-digests.json`). They are on the published-material denylist
  (`Codelab.Security/PublishedMaterial/published-material.fixtures.json`), so a production boot configured with any
  of them fails, and they are never used outside tests.
- **Scanner exception, not a free pass.** `tools/extract/secret-exceptions.txt` lets only the entropy and key-shape
  rules match in this folder; the same content anywhere else is a finding, and the digest denylist is never excepted.
- **Pinned prefix.** The checked-in set uses `CODELABTEST`. Other prefixes are generated on demand, never checked in.

## Gotchas

- Timestamps in cases have at most nine digits or do not start with 1 or 2: a ten-digit number starting with 1 or 2
  reads as a national id to the scrub sweep.
- PublicId ids are strings: JSON numbers lose precision above 2^53 in JavaScript.
- A changed case name changes its nonce (nonces are derived from the name), so every value of that case moves.
