# ADR-008: Handover kit (full dependency closure, offline npm cache)

- **Status:** Accepted
- **Date:** 2026-11-04
- **Deciders:** Amr Emam (platform owner)

## Context

Decision D7 requires every client handover to ship a local packages folder, a `nuget.config` and the package source code, so the client can build offline. ADR-002 puts the security core into `Codelab.*` NuGet and `@codelab/*` npm packages on an internal feed that clients cannot reach. Ministry build environments often have no internet access at all, so shipping only the Codelab packages is not enough: every third-party NuGet package and npm tarball the build resolves must also be in the kit. The plan schedules a v0 script in phase 1 and the final kit, including the offline build test on a disconnected VM, in phase 10.

## Decision

1. **Full closure, not Codelab-only.** `deploy/handover/New-HandoverKit.ps1` runs `dotnet restore --packages <kit>/nuget` (with `NUGET_PACKAGES` pointing at the same folder, so MSBuild SDK packages are captured too) for every listed project or solution. It then trims each `{id}/{version}` folder to the files a folder feed needs.
2. **One offline NuGet source.** The kit's `nuget.config` has `<clear/>` and a single folder source, `./nuget`. There is no nuget.org source and no package source mapping, because a single source leaves nothing to map.
3. **Offline npm through the npm cache.** For each npm install folder the script runs `npm ci --cache <kit>/npm-cache`, which caches every Codelab and third-party tarball the lock file names. The client runs `npm ci --offline --cache ./npm-cache`. Tarballs a lock file installs by `file:` path are shipped at their source-relative path.
4. **Fallback, recorded and not built:** if the npm cache approach fails for a client (for example an npm major version that changes the cache format, or a client that mandates a registry), the fallback is a Verdaccio storage export. That means a Verdaccio instance filled from the same lock files, with its `storage/` folder shipped and served on the client network.
5. **Package sources ship as zips.** There is one zip per folder under `packages/dotnet`, `packages/npm` and `packages/vectors`, with source-relative entry names and no build output.
6. **Prerequisites are listed, not shipped, for now.** `PREREQUISITES.md` gives the .NET SDK version from `global.json`, the Node version from `.nvmrc` and the Playwright browser versions ("none in P1"). Whether the kit ships the SDK, Node and browser installers or lists them as client prerequisites is settled in **P10**.
7. **Integrity and safety.**
   - `manifest.json` records the kit kind, the schema version, the inputs, the tool versions and the SHA-256 of every file.
   - The script deletes nothing outside its output folder. A non-empty folder is replaced only with `-Force`, and only when it holds a previous kit and nothing else.
8. **Proof.**
   - `deploy/handover/Test-HandoverKit.ps1` simulates no network: a clean copy of the source tree, the kit's `nuget.config` only, an empty `NUGET_PACKAGES`, proxies pointing at an unroutable local port and `npm ci --offline`.
   - The real proof, restoring and building on a VM with the network adapter disabled, is a **platform-owner step**. It is done for the package sample in phase 1 and for the whole starter in phase 10.
   - The simulation also runs `npm test` in each npm project that has one, so the installed `@codelab/ng-security` is imported and run offline, not only installed.
   - Phase 1 VM proof: **not yet run** (open, owner step). Record the date, the VM, the kit's `manifest.json` SHA-256 and the result here when it is done; until then the phase 1 exit criterion "network-disabled restore and build" is met only by the simulation.

## Consequences

- A kit is large (tens of MB for one package and its npm workspace) and is built per handover from the projects actually delivered. A project missing from `-Projects` means missing packages, which the offline test catches.
- The npm cache format belongs to npm. The kit records the npm version it was built with, and the Verdaccio fallback is the exit if a client's npm cannot read it.
- Shared frameworks (`Microsoft.AspNetCore.App`) come from the installed SDK, so the SDK version in `PREREQUISITES.md` is a hard requirement until P10 decides on shipping installers.
- The in-process offline test runs on any developer machine. It is a simulation, so the VM step remains necessary before a real handover.
