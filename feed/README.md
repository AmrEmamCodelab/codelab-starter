# feed/

Local package feed (until a hosted feed exists). Contents are gitignored; only the folders are tracked.

- `feed/nuget` - `.nupkg` files pushed by `tools/build.ps1 -Pack` (`dotnet nuget push --source feed/nuget`).
  `nuget.config` maps `Codelab.*` to this folder only.
- `feed/npm` - `npm pack` tarballs, installed by path: `npm install ./feed/npm/codelab-ng-tools-<version>.tgz`.

Moving to a hosted feed changes `nuget.config`, `.npmrc` and the push commands in `tools/build.ps1`, nothing else.
