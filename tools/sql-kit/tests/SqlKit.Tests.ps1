# Pester 3.4 tests for tools/sql-kit/SqlKit.psm1 (Windows PowerShell 5.1). Pure: no dotnet, no database, no files
# written. The fixtures are a synthetic EF Core idempotent script (schema [demo]) and its exact reshaped text.
#   powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester tools/sql-kit/tests/SqlKit.Tests.ps1 -EnableExit"
Set-StrictMode -Version Latest

$KitFolder = Split-Path -Parent $PSScriptRoot
$FixtureFolder = Join-Path $PSScriptRoot 'fixtures'
Import-Module (Join-Path $KitFolder 'SqlKit.psm1') -Force

function Read-Fixture([string] $Name) { return [IO.File]::ReadAllText((Join-Path $FixtureFolder $Name), (New-Object Text.UTF8Encoding($false))) }

# Git may check the fixtures out with LF or CRLF; the reshaper always writes CRLF.
function ConvertTo-Lf([string] $Text) { return $Text.Replace("`r`n", "`n") }

function New-Guarded([string] $MigrationId, [string[]] $Body) {
    $lines = @('IF NOT EXISTS (', '    SELECT * FROM [demo].[__EFMigrationsHistory]', "    WHERE [MigrationId] = N'$MigrationId'", ')', 'BEGIN') + $Body + @('END;')
    return ($lines -join "`r`n")
}

Describe 'ConvertTo-DbaSqlScript' {
    $raw = Read-Fixture 'ef-idempotent.sql'

    It 'reshapes the fixture exactly as expected-dba.sql' {
        $result = ConvertTo-DbaSqlScript -Sql $raw -ContextName 'DemoDbContext' -SourceRevision 'abc1234'
        (ConvertTo-Lf $result.Text) | Should BeExactly (ConvertTo-Lf (Read-Fixture 'expected-dba.sql'))
    }

    It 'writes CRLF line endings and no byte order mark (the writer adds it)' {
        $text = (ConvertTo-DbaSqlScript -Sql $raw -ContextName 'DemoDbContext').Text
        $text.Contains("`r`n") | Should Be $true
        ($text -replace "`r`n", '').Contains("`n") | Should Be $false
        $text[0] | Should Be '/'
    }

    It 'lists each migration id once, in order, and counts the guarded blocks' {
        $result = ConvertTo-DbaSqlScript -Sql $raw -ContextName 'DemoDbContext'
        ($result.MigrationIds -join ',') | Should BeExactly '20260101000000_InitialCreate,20260102000000_RenameColumn'
        $result.BlockCount | Should Be 7
    }

    It 'wraps each body in one EXEC with doubled quotes (twice for EF''s own nested EXEC)' {
        $sql = New-Guarded 'm1' @("    EXEC(N'INSERT INTO [demo].[T] VALUES (N''x'')');")
        $text = (ConvertTo-DbaSqlScript -Sql $sql -ContextName 'Demo').Text
        $text | Should Match ([regex]::Escape("    EXEC(N'EXEC(N''INSERT INTO [demo].[T] VALUES (N''''x'''')'');');"))
    }

    It 'removes only EF''s four-space indent and keeps the rest of the line as written' {
        $sql = New-Guarded 'm1' @("    SELECT N'a  ", "      b';")
        $text = (ConvertTo-DbaSqlScript -Sql $sql -ContextName 'Demo').Text
        $text | Should Match ([regex]::Escape("EXEC(N'SELECT N''a  `r`n  b'';');"))
    }

    It 'puts every block, BEGIN TRANSACTION and COMMIT in a batch of its own' {
        $sql = ('BEGIN TRANSACTION;', (New-Guarded 'm1' @('    SELECT 1;')), (New-Guarded 'm1' @('    SELECT 2;')), 'COMMIT;', 'GO') -join "`r`n"
        $text = ConvertTo-Lf (ConvertTo-DbaSqlScript -Sql $sql -ContextName 'Demo').Text
        $text | Should Match "BEGIN TRANSACTION;\nGO\n"
        $text | Should Match "EXEC\(N'SELECT 1;'\);\nEND;\nGO\n"
        $text | Should Match "EXEC\(N'SELECT 2;'\);\nEND;\nGO\n"
        $text | Should Match "COMMIT;\nGO\n"
    }

    It 'guards the run: SQLCMD mode required, stop on error, the session options SQL Server needs' {
        $text = ConvertTo-Lf (ConvertTo-DbaSqlScript -Sql $raw -ContextName 'Demo').Text
        $text | Should Match ':setvar CodelabSqlCmdMode "on"\nGO\n'
        $text | Should Match 'SET NOEXEC ON;'
        $text | Should Match ':on error exit\nSET NOCOUNT ON;\nSET XACT_ABORT ON;\nSET QUOTED_IDENTIFIER ON;'
        $text | Should Match 'SET NOEXEC OFF;\nGO\n$'
    }

    It 'never closes the header comment early' {
        $text = (ConvertTo-DbaSqlScript -Sql $raw -ContextName 'Odd*/Name' -SourceRevision 'r*/1').Text
        $header = $text.Substring(0, $text.IndexOf(':setvar'))
        ([regex]::Matches($header, [regex]::Escape('*/'))).Count | Should Be 1
    }

    It 'refuses a body sqlcmd would rewrite ($(name) is a sqlcmd variable)' {
        $sql = New-Guarded 'm1' @("    SELECT N'`$(Secret)';")
        { ConvertTo-DbaSqlScript -Sql $sql -ContextName 'Demo' } | Should Throw 'sqlcmd would substitute'
    }

    It 'reshapes an empty script into a file that only prints its summary' {
        $result = ConvertTo-DbaSqlScript -Sql '' -ContextName 'Demo'
        $result.BlockCount | Should Be 0
        $result.Text | Should Match '\(none\)'
    }
}

Describe 'Split-EfIdempotentScript' {
    It 'keeps an indented END; inside the body (only a column-0 END; closes the block)' {
        $sql = New-Guarded 'm1' @('    IF 1 = 1', '    BEGIN', '        SELECT 1;', '    END;', '    SELECT 2;')
        $segments = @(Split-EfIdempotentScript -Sql $sql)
        $segments.Count | Should Be 1
        $segments[0].Body.Count | Should Be 5
        $segments[0].MigrationId | Should Be 'm1'
    }

    It 'ignores a leading byte order mark, blank lines and GO' {
        $sql = ([string] [char] 0xFEFF) + "`r`nGO`r`n`r`nBEGIN TRANSACTION;`r`nCOMMIT;`r`ngo`r`n"
        (@(Split-EfIdempotentScript -Sql $sql) | ForEach-Object { $_.Kind }) -join ',' | Should BeExactly 'BeginTransaction,Commit'
    }

    It 'refuses an unknown top-level statement with its line number' {
        { Split-EfIdempotentScript -Sql "BEGIN TRANSACTION;`r`nDROP TABLE [demo].[T];`r`nCOMMIT;" } | Should Throw 'Line 2: unexpected top-level statement'
    }

    It 'refuses a block without BEGIN or without a column-0 END;' {
        { Split-EfIdempotentScript -Sql "IF 1 = 1`r`n    SELECT 1;" } | Should Throw 'has no BEGIN line'
        { Split-EfIdempotentScript -Sql "IF 1 = 1`r`nBEGIN`r`n    SELECT 1;`r`n    END;" } | Should Throw 'has no column-0 END;'
    }

    It 'refuses unbalanced transactions' {
        { Split-EfIdempotentScript -Sql "BEGIN TRANSACTION;`r`nBEGIN TRANSACTION;" } | Should Throw 'before the COMMIT'
        { Split-EfIdempotentScript -Sql 'COMMIT;' } | Should Throw 'without BEGIN TRANSACTION'
        { Split-EfIdempotentScript -Sql 'BEGIN TRANSACTION;' } | Should Throw 'never committed'
    }
}

Describe 'Contexts' {
    function New-Context([string] $Name, [string] $Assembly) { return [pscustomobject]@{ FullName = "$Assembly.Persistence.$Name"; Name = $Name; AssemblyName = $Assembly } }

    It 'reads the data lines of dotnet ef dbcontext list --json --prefix-output' {
        $lines = @(
            'warn:    a host warning'
            'data:    ['
            'data:      { "fullName": "Starter.Modules.Lookups.Infrastructure.Persistence.LookupsDbContext", "safeName": "LookupsDbContext", "name": "LookupsDbContext", "assemblyQualifiedName": "Starter.Modules.Lookups.Infrastructure.Persistence.LookupsDbContext, Starter.Modules.Lookups, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null" }'
            'data:    ]'
        )
        $contexts = @(ConvertFrom-EfContextList -OutputLines $lines)
        $contexts.Count | Should Be 1
        $contexts[0].Name | Should Be 'LookupsDbContext'
        $contexts[0].AssemblyName | Should Be 'Starter.Modules.Lookups'
    }

    It 'refuses output without data lines' {
        { ConvertFrom-EfContextList -OutputLines @('info: nothing') } | Should Throw 'no data lines'
    }

    It 'orders the kernel contexts first (listed order, then by name), then modules by module name (ordinal)' {
        $contexts = @(
            (New-Context 'UsersDbContext' 'Starter.Modules.Users')
            (New-Context 'NumberingDbContext' 'Starter.Infrastructure')
            (New-Context 'ExtraDbContext' 'Starter.Infrastructure')
            (New-Context 'AuditDbContext' 'Starter.Infrastructure')
            (New-Context 'AccessDbContext' 'Starter.Modules.Access')
            (New-Context 'OutboxDbContext' 'Starter.Infrastructure')
            (New-Context 'ServiceRequestsDbContext' 'Starter.Modules.ServiceRequests')
            (New-Context 'SettingsDbContext' 'Starter.Modules.Settings')
        )
        $ordered = @(Get-DbaContextOrder -Contexts $contexts | ForEach-Object { $_.Name }) -join ','
        $ordered | Should BeExactly 'AuditDbContext,OutboxDbContext,NumberingDbContext,ExtraDbContext,AccessDbContext,ServiceRequestsDbContext,SettingsDbContext,UsersDbContext'
    }

    It 'names the files NN-<Context>.sql' {
        Get-DbaScriptFileName -Position 1 -ContextName 'AuditDbContext' | Should BeExactly '01-Audit.sql'
        Get-DbaScriptFileName -Position 12 -ContextName 'Portal' | Should BeExactly '12-Portal.sql'
        Get-DbaScriptFileName -Position 3 -ContextName 'DbContext' | Should BeExactly '03-DbContext.sql'
    }
}
