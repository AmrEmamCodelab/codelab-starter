#Requires -Version 5.1
# SqlKit.psm1: turns EF Core idempotent migration scripts into DBA-runnable files (MODULE.md in this folder).
#   ConvertTo-DbaSqlScript     the reshaper: every guarded block's body runs through EXEC(N'...') in its own batch
#   Split-EfIdempotentScript   the parser behind it: EF's script as an ordered list of segments
#   ConvertFrom-EfContextList  `dotnet ef dbcontext list --json --prefix-output` output -> context objects
#   Get-DbaContextOrder        the order the API applies migrations in (kernel contexts, then modules by name)
#   Get-DbaScriptFileName      NN-<Context>.sql
# Pure functions only (no process, no file system): New-DbaSqlScripts.ps1 runs dotnet and writes the files.
# Windows PowerShell 5.1 compatible.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Enums (integers with explicit values) ------------------------------------------------------------------------
enum DbaSegmentKind {
    GuardedBlock = 1
    BeginTransaction = 2
    Commit = 3
}

# The order groups of Get-DbaContextOrder (compared as integers).
enum DbaContextGroup {
    ListedKernel = 1
    OtherKernel = 2
    Module = 3
}

# --- Constants ----------------------------------------------------------------------------------------------------
# EF Core's idempotent script (SqlServerMigrationsSqlGenerator): what may appear at the top level.
$script:BeginTransactionLine   = 'BEGIN TRANSACTION;'
$script:CommitLine             = 'COMMIT;'
$script:BatchSeparator         = 'GO'
$script:GuardPrefix            = 'IF '
$script:BlockOpenLine          = 'BEGIN'
$script:BlockCloseLine         = 'END;'
$script:BodyIndent             = '    '
$script:MigrationIdPattern     = "\[MigrationId\]\s*=\s*N'([^']+)'"
$script:LineSplitPattern       = '\r?\n'
$script:ByteOrderMark          = [char] 0xFEFF
$script:SingleQuote            = "'"
$script:DoubledQuote           = "''"
# sqlcmd substitutes $(name) everywhere in the file, string literals included; a body with it would change meaning.
$script:SqlCmdVariableToken    = '$('

# The DBA file.
$script:NewLine                = "`r`n"
$script:ExecOpen               = "    EXEC(N'"
$script:ExecClose              = "');"
$script:SqlCmdModeVariable     = 'CodelabSqlCmdMode'
$script:SqlCmdModeValue        = 'on'
$script:SessionOptions         = @(
    'SET NOCOUNT ON;'
    'SET XACT_ABORT ON;'
    'SET QUOTED_IDENTIFIER ON;'
    'SET ANSI_NULLS ON;'
    'SET ANSI_PADDING ON;'
    'SET ANSI_WARNINGS ON;'
    'SET ARITHABORT ON;'
    'SET CONCAT_NULL_YIELDS_NULL ON;'
    'SET NUMERIC_ROUNDABORT OFF;'
)
$script:CommentClose           = '*/'
$script:CommentCloseSafe       = '* /'

# Context discovery and order.
$script:EfDataPrefix           = 'data:'
$script:ContextSuffix          = 'DbContext'
$script:FileNameFormat         = '{0:D2}-{1}.sql'
$script:AssemblyNameIndex      = 1
$script:AssemblyNameSeparator  = ','
$script:DefaultModulePrefix    = 'Starter.Modules.'
# The kernel contexts in the order AddStarterKernel registers them (persistence/audit, messaging, numbering).
$script:DefaultKernelOrder     = @('AuditDbContext', 'OutboxDbContext', 'NumberingDbContext')

# ==================================================================================================================
# Parser
# ==================================================================================================================
function New-DbaSegment([DbaSegmentKind] $Kind, [string[]] $Condition = @(), [string[]] $Body = @(), [int] $Line = 0) {
    $ids = @(([regex]::Matches(($Condition -join "`n"), $script:MigrationIdPattern)) | ForEach-Object { $_.Groups[1].Value })
    $migrationId = if ($ids.Count -gt 0) { $ids[0] } else { $null }
    return [pscustomobject]@{ Kind = $Kind.ToString(); KindId = [int] $Kind; Condition = [string[]] $Condition; Body = [string[]] $Body; MigrationId = $migrationId; Line = $Line }
}

function Test-DbaLine([string] $Line, [string] $Expected) {
    return [string]::Equals($Line.TrimEnd(), $Expected, [StringComparison]::OrdinalIgnoreCase)
}

function Split-EfIdempotentScript {
    <#
    .SYNOPSIS
        Parses an EF Core idempotent migration script (dotnet ef migrations script --idempotent, SQL Server) into an
        ordered list of segments: GuardedBlock (an IF ... BEGIN ... END; block: Condition lines, Body lines, the
        MigrationId it guards), BeginTransaction and Commit. Blank lines and GO separators are dropped.
    .DESCRIPTION
        EF writes every statement as a top-level IF block whose body is indented; only a column-0 END; closes it, so an
        END; inside the body (indented by EF) never does. Anything else at the top level is refused with its line
        number: the reshaper never guesses.
    #>
    param([Parameter(Mandatory = $true)] [AllowEmptyString()] [string] $Sql)
    $lines = [regex]::Split($Sql.TrimStart($script:ByteOrderMark), $script:LineSplitPattern)
    $segments = New-Object 'System.Collections.Generic.List[object]'
    $index = 0
    while ($index -lt $lines.Length) {
        $line = $lines[$index]
        $number = $index + 1
        $index++
        if ([string]::IsNullOrWhiteSpace($line) -or (Test-DbaLine $line $script:BatchSeparator)) { continue }
        if (Test-DbaLine $line $script:BeginTransactionLine) { $segments.Add((New-DbaSegment BeginTransaction -Line $number)); continue }
        if (Test-DbaLine $line $script:CommitLine) { $segments.Add((New-DbaSegment Commit -Line $number)); continue }
        if (-not $line.StartsWith($script:GuardPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Line ${number}: unexpected top-level statement; only IF ... BEGIN ... END; blocks, BEGIN TRANSACTION;, COMMIT; and GO are reshaped."
        }
        $condition = New-Object 'System.Collections.Generic.List[string]'
        $condition.Add($line.TrimEnd())
        while ($index -lt $lines.Length -and -not (Test-DbaLine $lines[$index] $script:BlockOpenLine)) { $condition.Add($lines[$index].TrimEnd()); $index++ }
        if ($index -ge $lines.Length) { throw "Line ${number}: the IF block has no BEGIN line." }
        $index++
        $body = New-Object 'System.Collections.Generic.List[string]'
        while ($index -lt $lines.Length -and -not [string]::Equals($lines[$index].TrimEnd(), $script:BlockCloseLine, [StringComparison]::Ordinal)) { $body.Add($lines[$index]); $index++ }
        if ($index -ge $lines.Length) { throw "Line ${number}: the IF block has no column-0 END; line." }
        $index++
        $segments.Add((New-DbaSegment GuardedBlock -Condition $condition.ToArray() -Body $body.ToArray() -Line $number))
    }
    Assert-DbaTransactionsBalanced $segments.ToArray()
    return $segments.ToArray()
}

function Assert-DbaTransactionsBalanced([object[]] $Segments) {
    $open = $null
    foreach ($segment in $Segments) {
        if ($segment.KindId -eq [int] [DbaSegmentKind]::BeginTransaction) {
            if ($null -ne $open) { throw "Line $($segment.Line): BEGIN TRANSACTION; before the COMMIT; of the one at line $($open.Line)." }
            $open = $segment
        }
        elseif ($segment.KindId -eq [int] [DbaSegmentKind]::Commit) {
            if ($null -eq $open) { throw "Line $($segment.Line): COMMIT; without BEGIN TRANSACTION;." }
            $open = $null
        }
    }
    if ($null -ne $open) { throw "Line $($open.Line): BEGIN TRANSACTION; is never committed." }
}

# ==================================================================================================================
# Reshaper
# ==================================================================================================================
# EF indents every body line by four spaces (string literal lines included); removing them restores the text the
# migration wrote. Nothing else changes (trailing spaces inside a literal stay); leading and trailing blank lines go.
function Get-DbaBodyText([string[]] $Body) {
    $lines = @($Body | ForEach-Object { if ($_.StartsWith($script:BodyIndent)) { $_.Substring($script:BodyIndent.Length) } else { $_ } })
    $first = 0
    $last = $lines.Count - 1
    while ($first -le $last -and [string]::IsNullOrWhiteSpace($lines[$first])) { $first++ }
    while ($last -ge $first -and [string]::IsNullOrWhiteSpace($lines[$last])) { $last-- }
    if ($first -gt $last) { return '' }
    return ($lines[$first..$last] -join $script:NewLine)
}

function ConvertTo-DbaExecStatement([string] $BodyText, [int] $Line) {
    if ($BodyText.Contains($script:SqlCmdVariableToken)) {
        throw "Line ${Line}: the block contains '$($script:SqlCmdVariableToken)', which sqlcmd would substitute as a variable; change the migration text."
    }
    return $script:ExecOpen + $BodyText.Replace($script:SingleQuote, $script:DoubledQuote) + $script:ExecClose
}

function ConvertTo-DbaCommentText([string] $Text) { return $Text.Replace($script:CommentClose, $script:CommentCloseSafe) }

function Get-DbaHeader([string] $ContextName, [string] $SourceRevision, [string[]] $MigrationIds) {
    $lines = New-Object 'System.Collections.Generic.List[string]'
    $lines.Add('/* ' + (ConvertTo-DbaCommentText "Database migrations: $ContextName"))
    if (-not [string]::IsNullOrWhiteSpace($SourceRevision)) { $lines.Add('   ' + (ConvertTo-DbaCommentText "Source revision: $SourceRevision")) }
    $lines.Add('   Generated by tools/sql-kit/New-DbaSqlScripts.ps1 from dotnet ef migrations script --idempotent. Do not edit.')
    $lines.Add('')
    $lines.Add('   Idempotent: each migration runs only when its id is missing from the context''s __EFMigrationsHistory,')
    $lines.Add('   so the file is safe on a database at any earlier state and does nothing on an up-to-date one.')
    $lines.Add('   Every step is its own batch and runs through EXEC(N''...''), so a step that is already applied is never')
    $lines.Add('   compiled against the current schema. Each migration is one transaction: on an error the run stops')
    $lines.Add('   (:on error exit) and that migration is rolled back; earlier migrations stay applied, and a rerun')
    $lines.Add('   continues from there.')
    $lines.Add('')
    $lines.Add('   Run in SQLCMD mode (back up the database first):')
    $lines.Add('     sqlcmd -S <server> -d <database> -E -b -f 65001 -i <this file>')
    $lines.Add('     SSMS: Query > SQLCMD Mode, select the database, Execute. Without SQLCMD mode nothing is executed.')
    $lines.Add('')
    $lines.Add('   Migrations in this file:')
    foreach ($id in $MigrationIds) { $lines.Add('     ' + (ConvertTo-DbaCommentText $id)) }
    if ($MigrationIds.Count -eq 0) { $lines.Add('     (none)') }
    $lines.Add('*/')
    return $lines.ToArray()
}

function Get-DbaPrologue {
    $lines = New-Object 'System.Collections.Generic.List[string]'
    # Without SQLCMD mode the :setvar line fails, the variable stays unexpanded and NOEXEC turns every later batch off.
    $lines.Add((':setvar {0} "{1}"' -f $script:SqlCmdModeVariable, $script:SqlCmdModeValue))
    $lines.Add($script:BatchSeparator)
    $lines.Add(("IF N'`$({0})' <> N'{1}'" -f $script:SqlCmdModeVariable, $script:SqlCmdModeValue))
    $lines.Add('BEGIN')
    $lines.Add('    PRINT N''Enable SQLCMD mode (SSMS: Query > SQLCMD Mode) and run the file again; nothing was executed.'';')
    $lines.Add('    SET NOEXEC ON;')
    $lines.Add('END;')
    $lines.Add($script:BatchSeparator)
    $lines.Add(':on error exit')
    foreach ($option in $script:SessionOptions) { $lines.Add($option) }
    $lines.Add($script:BatchSeparator)
    return $lines.ToArray()
}

function ConvertTo-DbaSqlScript {
    <#
    .SYNOPSIS
        Reshapes one EF Core idempotent migration script into a DBA-runnable file (the text, CRLF line endings).
    .DESCRIPTION
        Every guarded block keeps its IF condition and BEGIN/END, and its body becomes one EXEC(N'...') statement
        (single quotes doubled, EF's four-space body indent removed) in a batch of its own (GO after each block).
        BEGIN TRANSACTION; and COMMIT; stay where EF put them, each followed by GO; with XACT_ABORT ON and
        :on error exit a failing step rolls its migration back and stops the run. A header lists the migrations; a
        prologue refuses to execute anything outside SQLCMD mode and sets the session options SQL Server needs for
        filtered indexes and computed columns (sqlcmd starts with QUOTED_IDENTIFIER OFF).
    .OUTPUTS
        [pscustomobject] Text, MigrationIds (string[], in order, unique), BlockCount.
    #>
    param(
        [Parameter(Mandatory = $true)] [AllowEmptyString()] [string] $Sql,
        [Parameter(Mandatory = $true)] [string] $ContextName,
        [string] $SourceRevision
    )
    $segments = @(Split-EfIdempotentScript -Sql $Sql)
    $migrationIds = New-Object 'System.Collections.Generic.List[string]'
    foreach ($segment in $segments) {
        if ($null -ne $segment.MigrationId -and -not $migrationIds.Contains($segment.MigrationId)) { $migrationIds.Add($segment.MigrationId) }
    }
    $out = New-Object 'System.Collections.Generic.List[string]'
    foreach ($line in (Get-DbaHeader -ContextName $ContextName -SourceRevision $SourceRevision -MigrationIds $migrationIds.ToArray())) { $out.Add($line) }
    foreach ($line in (Get-DbaPrologue)) { $out.Add($line) }
    $blocks = 0
    foreach ($segment in $segments) {
        $out.Add('')
        switch ([DbaSegmentKind] $segment.KindId) {
            ([DbaSegmentKind]::BeginTransaction) { $out.Add($script:BeginTransactionLine) }
            ([DbaSegmentKind]::Commit) { $out.Add($script:CommitLine) }
            ([DbaSegmentKind]::GuardedBlock) {
                foreach ($line in $segment.Condition) { $out.Add($line) }
                $out.Add($script:BlockOpenLine)
                $out.Add((ConvertTo-DbaExecStatement -BodyText (Get-DbaBodyText $segment.Body) -Line $segment.Line))
                $out.Add($script:BlockCloseLine)
                $blocks++
            }
        }
        $out.Add($script:BatchSeparator)
    }
    $out.Add('')
    $out.Add(("PRINT N'{0}: migrations are up to date.';" -f $ContextName.Replace($script:SingleQuote, $script:DoubledQuote)))
    $out.Add($script:BatchSeparator)
    $out.Add('SET NOEXEC OFF;')
    $out.Add($script:BatchSeparator)
    return [pscustomobject]@{ Text = (($out.ToArray() -join $script:NewLine) + $script:NewLine); MigrationIds = [string[]] $migrationIds.ToArray(); BlockCount = $blocks }
}

# ==================================================================================================================
# Contexts: discovery output, order, file names
# ==================================================================================================================
function ConvertFrom-EfContextList {
    <#
    .SYNOPSIS
        Reads `dotnet ef dbcontext list --json --prefix-output` output (the lines starting with 'data:') into
        objects: FullName, Name (the type name), AssemblyName.
    #>
    param([Parameter(Mandatory = $true)] [AllowEmptyCollection()] [string[]] $OutputLines)
    $json = @($OutputLines | Where-Object { $_.StartsWith($script:EfDataPrefix, [StringComparison]::Ordinal) } |
        ForEach-Object { $_.Substring($script:EfDataPrefix.Length).Trim() }) -join "`n"
    if ([string]::IsNullOrWhiteSpace($json)) { throw 'dotnet ef dbcontext list returned no data lines; read its output above.' }
    $items = @(ConvertFrom-Json -InputObject $json | ForEach-Object { $_ })
    return @($items | ForEach-Object {
        $assembly = ([string] $_.assemblyQualifiedName).Split($script:AssemblyNameSeparator)[$script:AssemblyNameIndex].Trim()
        [pscustomobject]@{ FullName = [string] $_.fullName; Name = [string] $_.name; AssemblyName = $assembly }
    })
}

function Get-DbaContextOrder {
    <#
    .SYNOPSIS
        Orders contexts the way the API migrates them: kernel contexts (assemblies outside -ModulePrefix) first, in
        -KernelOrder then by name; then module contexts by module name (ModuleCatalog sorts modules by name,
        ordinal), then by context name.
    #>
    param(
        [Parameter(Mandatory = $true)] [AllowEmptyCollection()] [object[]] $Contexts,
        [string] $ModulePrefix = $script:DefaultModulePrefix,
        [string[]] $KernelOrder = $script:DefaultKernelOrder
    )
    $keyed = foreach ($context in $Contexts) {
        $isModule = $context.AssemblyName.StartsWith($ModulePrefix, [StringComparison]::Ordinal)
        $rank = [array]::IndexOf($KernelOrder, $context.Name)
        $group = if ($isModule) { [DbaContextGroup]::Module } elseif ($rank -ge 0) { [DbaContextGroup]::ListedKernel } else { [DbaContextGroup]::OtherKernel }
        $module = if ($isModule) { $context.AssemblyName.Substring($ModulePrefix.Length) } else { '' }
        [pscustomobject]@{ Group = [int] $group; Rank = $rank; Module = $module; Context = $context }
    }
    $list = New-Object 'System.Collections.Generic.List[object]'
    foreach ($item in @($keyed)) { $list.Add($item) }
    $list.Sort([Comparison[object]] {
        param($left, $right)
        if ($left.Group -ne $right.Group) { return $left.Group.CompareTo($right.Group) }
        if ($left.Rank -ne $right.Rank) { return $left.Rank.CompareTo($right.Rank) }
        $byModule = [string]::CompareOrdinal($left.Module, $right.Module)
        if ($byModule -ne 0) { return $byModule }
        return [string]::CompareOrdinal($left.Context.Name, $right.Context.Name)
    })
    return @($list | ForEach-Object { $_.Context })
}

function Get-DbaScriptFileName {
    <# .SYNOPSIS  NN-<Context>.sql: the 1-based position (two digits) and the context name without 'DbContext'. #>
    param([Parameter(Mandatory = $true)] [int] $Position, [Parameter(Mandatory = $true)] [string] $ContextName)
    $short = $ContextName
    if ($short.EndsWith($script:ContextSuffix, [StringComparison]::Ordinal) -and $short.Length -gt $script:ContextSuffix.Length) {
        $short = $short.Substring(0, $short.Length - $script:ContextSuffix.Length)
    }
    return ($script:FileNameFormat -f $Position, $short)
}

Export-ModuleMember -Function ConvertTo-DbaSqlScript, Split-EfIdempotentScript, ConvertFrom-EfContextList, Get-DbaContextOrder, Get-DbaScriptFileName
