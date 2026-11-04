# ADR-002: Package consumption (monorepo source switch, local feed now)

- **Status:** Accepted
- **Date:** 2026-11-04
- **Deciders:** Amr Emam (platform owner)

## Context

ADR-000 puts the security core into packages. During the build-out those packages change daily together with the code that uses them, and publishing on every change would slow development. Forks, however, must consume fixed, versioned packages and never carry package source. Decision D7 requires an internal feed and an offline handover kit. No repository host exists yet, so a hosted feed is not available.

## Decision

1. **Monorepo during the build-out.** Package sources live in `packages/dotnet/*` and `packages/npm/*` in the starter.
2. **MSBuild switch `UseCodelabPackageSource`.** It is set in `Directory.Build.props` and defaults to `true`. With `true`, Codelab.Build.Sdk is imported from source, and from phase 1 the `Codelab.*` `PackageReference`s become `ProjectReference`s. With `false`, everything resolves from the feed at pinned versions (for the SDK, `CodelabBuildSdkVersion`). CI also builds once with the switch off, against the feed versions.
3. **npm workspaces** (`packages/npm/*` in the root `package.json`) link `@codelab/*` during development.
4. **The feed is a local folder for now.** `feed\nuget` holds packed `.nupkg` files and `feed\npm` holds `npm pack` tarballs. `tools\build.ps1 -Pack` fills both with `0.x.y-ci.<n>` prereleases. `nuget.config` maps `Codelab.*` only to the local feed and everything else to nuget.org, using package source mapping so there is no dependency-confusion path. **GitHub Packages (NuGet and npm) replaces the folder later**, when the repo moves to GitHub (ADR-007). Only `nuget.config`, `.npmrc` and the pack step change.
5. **Forks.** `init.ps1` deletes `packages/` and pins exact feed versions, and `tools/upstream-merge.ps1` treats `packages/**` as a removed path. Forks receive package fixes only by version bumps (D3).
6. Splitting `packages/` into a separate `codelab-platform` repository is revisited after v1.0.

## Consequences

- There is no publish step while developing, and one solution builds everything.
- Two build modes must stay green, so CI runs both.
- From phase 1, the handover kit ships the full dependency closure (NuGet folder, npm cache and package sources) so a client can build offline.
- A version already in the feed cannot be pushed again, so the CI number increments.
