# deploy/kits

IIS deploy kits for the on-premises Windows Server targets. Each kit installs one deployable on one server, and
`-WhatIf` previews every change without making it.

| File | What it is |
| --- | --- |
| `Install-Common.psm1` | The shared module every kit is built on: prerequisites, backup, settings merge (keep server values, add only new keys), payload copy and the IIS app helper. See [MODULE.md](MODULE.md). |
| `Install-Attachment.ps1` | The attachment service kit (phase 6), the first kit built on the module. |
| `tests/` | Pester 3.4 tests for the module, with merge fixtures in `tests/fixtures/common/`. |

The API, web, gateway and Ops kits follow in phase 8 and are built on the same module.

```powershell
# Module tests (no IIS change, no admin)
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester deploy/kits/tests/Install-Common.Tests.ps1 -EnableExit"
```

Every kit follows the same order: check prerequisites, plan IIS, back up, copy the package, merge settings, and
check health. A kit never ships values that replace server settings, and never carries hosts, accounts, keys or
certificates. Those stay on the server.

The offline client handover kit is a different thing, and lives in [../handover](../handover/README.md).
