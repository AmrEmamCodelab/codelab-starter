# tools/ci/presets

## Purpose

The CI preset matrix of build plan phase 10. Each preset is an `init.ps1 -ConfigFile` file; `preset-leg.mjs` turns
one preset into a CI leg, and `.github/workflows/presets.yml` runs one leg per preset (ADR-007: the workflow only
calls repo scripts).

| Preset | Login modes | Optional modules |
| --- | --- | --- |
| `all.json` | basic, ad, sso | attachments, portal, sms, ops |
| `core-only.json` | basic | none |
| `sso-only.json` | sso | attachments, portal, sms, ops |

A leg: `git clone` the starter into `<work>\fork`, copy `feed\nuget` and `feed\npm` into it (pack them first with
`tools\build.ps1 -Pack`), `npm ci`, `init.ps1 -ConfigFile <preset> [-SkipDatabase]`, `tools/ci/fork-checks.mjs`
(package consumption), `tools/devseed/Test-NoFixtureKeys.mjs` (no fixture keys), then `tools\build.ps1 -Target CI`
in the fork. The checks are the starter's copies, run against the fork.

## Use

```powershell
node tools/ci/presets/preset-leg.mjs plan --preset core-only                 # the steps as JSON; changes nothing
node tools/ci/presets/preset-leg.mjs run --preset core-only --work D:\tmp\leg # runs the leg; exit 0/1/2
node tools/ci/fork-checks.mjs --root <fork> [--kit <kit dir>] [--allow-source <hosted feed url prefix>]
node --test tools/ci/tests/presets.test.mjs tools/ci/tests/fork-checks.test.mjs
```

## Extend

- **New preset:** add `<name>.json` here and add the name to the `preset:` matrix of `presets.yml`; the tests fail
  until both match.
- **Hosted feed (ADR-007 move):** pass its URL prefix to `fork-checks.mjs --allow-source` in the leg.

## Rules

- Presets follow the init config contract exactly (`validatePresetConfig`); CI presets skip the database.
- The leg never deletes: an existing `<work>\fork` is refused, and a work folder overlapping the repo is refused.
- Exit codes: 0 passed, 1 a step failed (named), 2 usage error, invalid preset or refused work folder.

## Gotchas

- The clone takes committed files only: uncommitted changes are not in the fork.
- init commits in the fork; the leg sets a CI git identity when the runner has none.
