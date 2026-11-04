# feed/

Local package feed (until a hosted feed exists). Contents are gitignored; only the folders are tracked.

- `feed/nuget` - `.nupkg` files pushed by `tools/build.ps1 -Pack` (`dotnet nuget push --source feed/nuget`).
  `nuget.config` maps `Codelab.*` to this folder only.
- `feed/npm` - `npm pack` tarballs, installed by path: `npm install ./feed/npm/codelab-ng-tools-<version>.tgz`.

Each run packs every package in `packages/versions.json`:

- 6 NuGet packages: Codelab.Build.Sdk, Codelab.Security, .Observability, .Errors, .Integration and .Testing.
- 2 npm packages: @codelab/ng-security and @codelab/ng-tools.

A run stamps them all as `<version>-ci.<n>`, with one shared `<n>`. `-Pack -Release` writes the exact versions. It
refuses any version that is already here, because a version in the feed is never replaced. See `tools/README.md`,
"Pack and release" and "Versioning".

Moving to a hosted feed changes `nuget.config`, `.npmrc` and the push commands in `tools/build.ps1`, nothing else.
