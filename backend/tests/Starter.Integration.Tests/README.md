# Starter.Integration.Tests

The integration tier: the real host on a throwaway SQL Server database.

## What it does

Each fixture (`DevelopmentHost`, `ProductionHost`):

1. starts LocalDB when the server is LocalDB (`sqllocaldb start MSSQLLocalDB`, a no-op when it runs);
2. creates `StarterIt_<guid>` (the solution token comes from the host assembly, so a renamed fork gets `<Token>It_<guid>`);
3. starts the host with accounts and keys generated at run time, which migrates every module DbContext, creates the
   Codelab.Security store tables and seeds accounts through the startup tasks (outside Development the fixture
   migrates first, because the kernel refuses to migrate there);
4. drops ONLY that database at the end, also when the start or a test failed. `TestSqlServer.RequireTestName`
   refuses to create or drop any name without the `It_` marker.

Covered: break-glass login and `GET /api/dashboard` 200; every catalog permission for break-glass; the Development
user's `GET /api/access/admin` 403 problem+json; logout 204 then the replayed access token 401; refresh rotation with
the old token refused (and the session ended); a wrong password as a refusal problem+json; the dev user absent in
Production; `/api/features` flags only; module schemas, migration histories and security-store tables.

## Use

```powershell
E:\codelab-tools\dotnet10\dotnet.exe test backend/tests/Starter.Integration.Tests
$env:CODELAB_TEST_SQL = 'myserver\instance'   # optional: another server (integrated security); a connection string such as 'Server=(localdb)\Other;Integrated Security=true' also works (its data source is used), so step 6 can share the value with the package SQL tests
```

## Extend

Add a test class with `[Collection(DevelopmentHostGroup.Name)]` (or the Production group) and drive the API through
`StarterClient`. A new environment-specific behaviour gets its own `IntegrationHost` subclass and collection.

## Gotchas

- Collections run one after another (`xunit.runner.json`), so at most one test database exists at a time.
- Request and response signing are Off here; signing is covered by the Codelab.Security golden vectors and the
  browser check.
- Run it from a normal shell. A LocalDB instance that is first started from inside a restricted sandbox can be
  unreachable for every other process (named-pipe error 121) until it is stopped with `sqllocaldb stop MSSQLLocalDB -k`.
- A process killed mid-run leaves its `StarterIt_*` database behind; drop it by hand.
