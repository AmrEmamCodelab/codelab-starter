# Attachment service

A separately released ASP.NET Core service that stores uploaded files on a file share. Browsers upload to it directly
with a one-time ticket from the API; the API reads and deletes files over the ES256 service-to-service scheme. It has
no database.

- **What it does, its configuration, rules and gotchas:** [MODULE.md](MODULE.md).
- **The browser upload contract** (ticket header, multipart shape, response, refusal codes): [docs/upload-api.md](docs/upload-api.md).

## Run

```powershell
powershell -File tools\devseed\New-LocalSecrets.ps1          # once (writes this service's local secrets too)
& E:\codelab-tools\dotnet10\dotnet.exe run --project services\attachment-service\src\Codelab.Attachments.Api
# http://localhost:5185/health/ready
```

## Build and test

```powershell
& E:\codelab-tools\dotnet10\dotnet.exe build services\attachment-service\Codelab.Attachments.slnx -warnaserror
& E:\codelab-tools\dotnet10\dotnet.exe test services\attachment-service\Codelab.Attachments.slnx
```

The tests need no SQL Server and no network: keys are generated per run and every file goes to a temp folder that the
test removes.

## Deploy

Publish `src/Codelab.Attachments.Api` (framework-dependent) and install it as an IIS application with
`deploy/kits/Install-Attachment.ps1`. The shipped `web.config` is a template with no secrets; supply the key rings,
the trusted service key and `Attachments:Storage:RootPath` from the app pool environment or a secret store.

## Layout

```
services/attachment-service/
  Codelab.Attachments.slnx
  src/Codelab.Attachments.Core/     store + upload pipeline (no host)
  src/Codelab.Attachments.Api/      the host: Program.cs, Authentication/, Endpoints/, Hosting/, web.config
  tests/Codelab.Attachments.Core.Tests/
  tests/Codelab.Attachments.Api.Tests/   HTTP security tests (WebApplicationFactory) + host unit tests
  docs/upload-api.md
```
