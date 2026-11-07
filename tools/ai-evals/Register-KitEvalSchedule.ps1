<#
.SYNOPSIS
    Plans (default) or registers a Windows scheduled task, for the current user without admin rights, that runs the
    full kit eval: every fixture through Invoke-KitEval.ps1 with -RequireCriteria.

.DESCRIPTION
    Without -Register this prints the task it would create and changes nothing (the default is a what-if). Registering
    is the platform owner's decision: the eval spends Claude usage on every run (open owner decision A1).
    Evidence of each scheduled run goes to <EvidenceRoot>\<yyyyMMdd-HHmm>\ (outside the repository by default).

    Exit codes: 0 planned or registered; 2 usage error.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\ai-evals\Register-KitEvalSchedule.ps1
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\ai-evals\Register-KitEvalSchedule.ps1 -DayOfWeek Friday -At 21:00 -Register
#>
[CmdletBinding()]
param(
    [string] $TaskName = 'Codelab Kit Eval',
    [ValidateSet('Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday')][string] $DayOfWeek = 'Saturday',
    [ValidatePattern('^([01][0-9]|2[0-3]):[0-5][0-9]$')][string] $At = '02:00',
    [ValidateRange(1, 100)][int] $Runs = 1,
    [string] $EvidenceRoot,
    [string] $RepoRoot,
    [switch] $Register
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ExitDone = 0
$TaskPath = '\Codelab\'
$PowerShellExecutable = 'powershell.exe'
$EvidenceFolderName = 'codelab-ai-evals'
$RunnerScriptName = 'Invoke-KitEval.ps1'
$ExecutionTimeLimitHours = 12
$TaskDescription = 'Codelab starter kit eval: every fixture through Claude Code headless, graded; pass rate >= 90% over >= 10 runs.'

function Get-DefaultEvidenceRoot { return (Join-Path $env:LOCALAPPDATA $EvidenceFolderName) }
function Get-DefaultRepoRoot { return (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) }

# The command the task runs (pure). The evidence folder is stamped at run time, inside the task's own command.
function Get-KitEvalScheduleCommand {
    param([Parameter(Mandatory = $true)][string] $Root, [Parameter(Mandatory = $true)][string] $Evidence, [Parameter(Mandatory = $true)][int] $RunCount)
    $runner = [IO.Path]::Combine($Root, 'tools', 'ai-evals', $RunnerScriptName)
    $script = "& '$runner' -Fixture all -Runs $RunCount -RequireCriteria -EvidenceDir (Join-Path '$Evidence' (Get-Date -Format 'yyyyMMdd-HHmm')); exit `$LASTEXITCODE"
    return [pscustomobject]@{
        Execute          = $PowerShellExecutable
        Argument         = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command `"$script`""
        WorkingDirectory = $Root
    }
}

# The full task definition (pure): current user, interactive logon (no stored password, no admin), weekly trigger.
function Get-KitEvalScheduleDefinition {
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $Day,
        [Parameter(Mandatory = $true)][string] $Time,
        [Parameter(Mandatory = $true)][int] $RunCount,
        [Parameter(Mandatory = $true)][string] $Evidence,
        [Parameter(Mandatory = $true)][string] $Root,
        [string] $UserId = ('{0}\{1}' -f $env:USERDOMAIN, $env:USERNAME)
    )
    return [pscustomobject]@{
        TaskName                = $Name
        TaskPath                = $TaskPath
        Description             = $TaskDescription
        UserId                  = $UserId
        LogonType               = 'Interactive'
        RunLevel                = 'Limited'
        DayOfWeek               = $Day
        At                      = $Time
        ExecutionTimeLimitHours = $ExecutionTimeLimitHours
        Action                  = Get-KitEvalScheduleCommand -Root $Root -Evidence $Evidence -RunCount $RunCount
    }
}

function Write-SchedulePlan([object] $Definition) {
    Write-Host "Scheduled task plan (what-if: nothing registered; pass -Register to create it)"
    Write-Host "  name:     $($Definition.TaskPath)$($Definition.TaskName)"
    Write-Host "  user:     $($Definition.UserId) (logon $($Definition.LogonType), run level $($Definition.RunLevel); no admin, no stored password)"
    Write-Host "  trigger:  weekly on $($Definition.DayOfWeek) at $($Definition.At); stops after $($Definition.ExecutionTimeLimitHours) h"
    Write-Host "  run:      $($Definition.Action.Execute) $($Definition.Action.Argument)"
    Write-Host "  in:       $($Definition.Action.WorkingDirectory)"
}

function Register-KitEvalTask([object] $Definition) {
    $action = New-ScheduledTaskAction -Execute $Definition.Action.Execute -Argument $Definition.Action.Argument -WorkingDirectory $Definition.Action.WorkingDirectory
    $trigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek $Definition.DayOfWeek -At $Definition.At
    $principal = New-ScheduledTaskPrincipal -UserId $Definition.UserId -LogonType $Definition.LogonType -RunLevel $Definition.RunLevel
    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Hours $Definition.ExecutionTimeLimitHours) -StartWhenAvailable -DontStopOnIdleEnd
    Register-ScheduledTask -TaskName $Definition.TaskName -TaskPath $Definition.TaskPath -Description $Definition.Description `
        -Action $action -Trigger $trigger -Principal $principal -Settings $settings | Out-Null
    Write-Host "Registered $($Definition.TaskPath)$($Definition.TaskName)."
}

function Invoke-Main {
    $root = if ($RepoRoot) { [IO.Path]::GetFullPath($RepoRoot) } else { Get-DefaultRepoRoot }
    $evidence = if ($EvidenceRoot) { [IO.Path]::GetFullPath($EvidenceRoot) } else { Get-DefaultEvidenceRoot }
    $definition = Get-KitEvalScheduleDefinition -Name $TaskName -Day $DayOfWeek -Time $At -RunCount $Runs -Evidence $evidence -Root $root
    Write-SchedulePlan $definition
    if ($Register) { Register-KitEvalTask $definition }
    return $ExitDone
}

# Dot-sourcing (the tests) defines the functions without running anything.
if ($MyInvocation.InvocationName -ne '.') { exit (Invoke-Main) }
