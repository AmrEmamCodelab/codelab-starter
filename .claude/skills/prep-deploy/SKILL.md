---
name: prep-deploy
description: "Prepare a deploy kit for an on-premises IIS + SQL Server environment: build publish/deploy-<sha>/ with tools/build.ps1 -Kit (API, Attachment service, web, optional gateway and Ops Console, DBA SQL scripts, the Install-* scripts, README and a hashed manifest), prove it, and walk the operator through the Install-*.ps1 runs (-WhatIf first, required-settings gaps, health). Use when the work is to prepare, package, build or ship a release, deployment or deploy kit, or to explain how to install one on the servers."
---

# Prep a deploy kit

Load the `coding-standards` skill first ([docs/standards/SKILL.md](../../../docs/standards/SKILL.md)) if anything in
the code has to change. The builder is documented in [tools/kit/MODULE.md](../../../tools/kit/MODULE.md); the install
scripts in [deploy/kits/MODULE.md](../../../deploy/kits/MODULE.md).

A kit is applied by hand on the servers, so everything is proven here first. **This skill never handles server
details:** no host names, IP addresses, accounts, passwords, keys, certificates or connection strings go into the
repo, the kit, the chat or a commit. Where a command needs one, write a placeholder in angle brackets
(`<site name>`, `<https base url>`) and let the operator fill it in on the server.

## Inputs (ask for anything missing, in one message)

1. **What to ship:** the commit (default `HEAD` of the release branch) and the components: `api`, `attachment`,
   `web` (the default set), plus `gateway` (DMZ host) and `ops` (Ops Console) when the environment runs them.
2. **The baseline:** the kit that is live now (its `manifest.json` `commit`), so the change list and the migrations
   can be compared. A first install has none.
3. **Who runs what:** the DBA runs `sql\`; an operator with local admin runs each `Install-*.ps1` on its server.

## Workflow (keep this checklist in your replies)

- [ ] **1. Clean, green source.** The work tree is committed (`git status --porcelain` is empty; a dirty tree is
      refused unless `-AllowDirty`, which marks the kit "not for production"). Run the CI contract or confirm it ran
      green on this commit: `powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Target CI`.
- [ ] **2. Plan the kit.**
      `powershell -NoProfile -ExecutionPolicy Bypass -File tools\kit\New-DeployKit.ps1 -DryRun -Component <list>`
      prints the kit folder, every payload with its source project, and whether `sql\` is generated. Fix any
      warning (a component whose `deploy\kits\Install-*.ps1` is missing is refused by the real build).
- [ ] **3. Build the kit.**
      `powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Kit [-KitComponent <list>]`
      (or `-Target CI -Kit`: the kit is built only when CI is green). It uses the private .NET SDK and Node and
      writes `publish\deploy-<shortsha>\` (gitignored): the payload folders, `kits\`, `sql\`, `README.md` and
      `manifest.json`.
- [ ] **4. Prove the kit.** In the kit folder:
      - `manifest.json`: `commit` is the intended commit, `dirty` is `false`, `packages` match
        `packages/versions.json`, `components` are the ones asked for, `sql.status` is `Included` (or the reason is
        understood and accepted).
      - Run the hash check from the kit README: it must print nothing.
      - No settings values ship: the payloads hold no `appsettings.Development*.json` or `*.local.json`, and no key
        material (the builder refuses those; confirm the summary).
      - Run each `kits\Install-*.ps1 -PackagePath .\<folder> ... -WhatIf` on a test machine if one exists; a preview
        needs no admin and changes nothing.
- [ ] **5. What changed since the baseline.** `git diff --name-only <baseline>..<commit>` per component path
      (`backend/`, `services/attachment-service/`, `services/integration-gateway/`, `frontend/starter-web/`,
      `services/ops-console/`). List new settings keys: each install script adds them from its template with an
      empty value and reports them as **required settings gaps** until the operator fills them on the server.
      Write the list into the hand-over message (key paths only, never values).
- [ ] **6. Database.** When `sql\` is present: the DBA runs the `NN-<context>.sql` files in file-name order. Each is
      idempotent (safe to rerun), so a partial run is finished by running the same files again. Without `sql\`,
      say so in the hand-over and agree how the schema is brought up to date before the API starts.
- [ ] **7. Hand over.** Zip `publish\deploy-<shortsha>\` (or copy the folder) and give the operator the README's
      install order. Report what was verified (CI, dry run, hashes, previews) and what was not.

## What the operator runs (from the kit folder, elevated Windows PowerShell 5.1)

Order: database, then the API, the Attachment service, the gateway (DMZ server), the web front site, the Ops
Console. Every script imports `kits\Install-Common.psm1`, keeps the server's settings files and adds only new keys,
supports `-WhatIf`, and checks `/health/ready` over HTTPS with certificate validation on.

```powershell
.\kits\Install-Api.ps1        -PackagePath .\api        -SiteName '<api site>'     -HealthBaseUrl '<https base url>' -WhatIf
.\kits\Install-Attachment.ps1 -PackagePath .\attachment -SiteName '<api site>'     -StorageRoot '<storage folder>'   -WhatIf
.\kits\Install-Gateway.ps1    -PackagePath .\gateway    -SiteName '<gateway site>' -HostName '<gateway host>' -CertificateThumbprint '<thumbprint>' -WhatIf
.\kits\Install-Web.ps1        -PackagePath .\web        -SiteName '<front site>'   -HealthBaseUrl '<https base url>' -WhatIf
.\kits\Install-Ops.ps1        -PackagePath .\ops        -SiteName '<api site>'     -HealthBaseUrl '<https base url>' -WhatIf
```

Then the same command without `-WhatIf`. `Get-Help .\kits\Install-<Name>.ps1 -Full` lists every parameter.

| Result | Meaning | Next step |
| --- | --- | --- |
| exit 0, health green | installed and ready | the next script |
| exit 2, "required settings" listed | the settings named are empty: the application is left stopped (`Install-Web.ps1` deploys nothing yet) | fill those keys in the server's own `appsettings.Production.json` (never in the kit), rerun the same command |
| exit 1, "refused" | a prerequisite or parameter check failed; nothing changed | fix what the message names (Hosting Bundle, site name, elevation), rerun |
| exit 1 after files changed | a later step failed | the script printed its backup folder; a rollback copies `<backup>\site\*` back |

## Rules

- Never print, store or commit a server's settings values; ask for key paths, not files. If a user pastes a
  settings file, use only its key names and do not repeat the values.
- Never edit a kit by hand after it is built: the manifest hashes would no longer match. Rebuild with `-Force`.
- Never ship a kit built with `-AllowDirty` to production.
- The kit carries templates and scripts only; environment values (hosts, origins, keys, connection strings, the
  Seq API key) are server-side gaps the install scripts report.

## When something fails

- **`New-DeployKit.ps1` exits 2:** an unknown `-Component` or a bad `-Sha`; the message lists the valid values.
- **"already exists; pass -Force":** a kit for this commit exists. `-Force` replaces it only when it holds the
  builder's own manifest.
- **"needs deploy\kits\Install-<Name>.ps1":** the component has no install script yet; drop it from `-Component`.
- **The DBA script generator is missing:** the kit is built without `sql\` and says so; agree on the schema step
  before handing over.
