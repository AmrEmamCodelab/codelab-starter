# Build offline from a handover kit

The kit holds every NuGet package and npm tarball the delivered source needs (ADR-008). The installers are
**listed, not shipped**: `PREREQUISITES.md` and `prerequisites.json` give the exact version, download URL and
SHA-256 of each.

1. **On a connected machine,** download each installer in `prerequisites.json` and check it:
   `(Get-FileHash <file> -Algorithm SHA256).Hash` must equal the listed `sha256` (case-insensitive).
2. **On the build machine,** install the .NET SDK and Node.js from those files. Playwright needs no download: the
   e2e runtime uses the installed Microsoft Edge.
3. **Copy** the delivered source tree and the kit folder to the build machine (for example `C:\src` and `C:\kit`).
4. **Check the kit:** `Test-HandoverKit.ps1 -KitPath C:\kit -SourceRoot C:\src` (exit 0 = restore and build pass
   from the kit alone). It checks every file hash against `manifest.json` and warns when the installed SDK or Node
   differs from the listed one.
5. **NuGet:** copy `C:\kit\nuget.config` over `C:\src\nuget.config`, change its folder source to `C:\kit\nuget`,
   then run `dotnet restore <solution>` and `dotnet build <solution> --no-restore` for each solution in
   `manifest.json` `projects`.
6. **npm:** copy every file under `C:\kit\npm-files` to the same path under `C:\src`. In each install folder named
   in `OFFLINE.md`, run `npm ci --offline --cache C:\kit\npm-cache`, then `npm run build` in each project in
   `manifest.json` `npmProjects`, in the listed order, then `npm rebuild --ignore-scripts --offline` in each install
   folder (it links the bins that point into a package's `dist\`).
7. **Codelab package sources** are in `C:\kit\sources\*.zip`; extract them at one root to rebuild `packages\`.

The kit's network-free proof on a VM with the network adapter disabled is a platform-owner step (ADR-008).
