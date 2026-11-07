# Pester 3.4 tests for tools/ai-evals/Register-KitEvalSchedule.ps1 (Windows PowerShell 5.1). Nothing is ever
# registered: the ScheduledTasks cmdlets are mocked, and the default (no -Register) must not call them at all.
#   powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester tools/ai-evals/tests -EnableExit"
$SchedulePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'Register-KitEvalSchedule.ps1'
. $SchedulePath
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

Describe 'Get-KitEvalScheduleDefinition' {
    $definition = Get-KitEvalScheduleDefinition -Name 'Kit Eval Test' -Day 'Friday' -Time '21:30' -RunCount 2 -Evidence 'X:\evidence' -Root 'X:\repo' -UserId 'HOST\someone'

    It 'runs as the current user with an interactive logon and a limited token (no admin, no stored password)' {
        $definition.UserId | Should Be 'HOST\someone'
        $definition.LogonType | Should Be 'Interactive'
        $definition.RunLevel | Should Be 'Limited'
    }
    It 'triggers weekly at the given day and time under the Codelab folder' {
        $definition.DayOfWeek | Should Be 'Friday'
        $definition.At | Should Be '21:30'
        $definition.TaskPath | Should Be '\Codelab\'
    }
    It 'runs every fixture through the runner with the criterion and a dated evidence folder' {
        $definition.Action.Execute | Should Be 'powershell.exe'
        $definition.Action.WorkingDirectory | Should Be 'X:\repo'
        $definition.Action.Argument | Should Match ([regex]::Escape("X:\repo\tools\ai-evals\Invoke-KitEval.ps1"))
        $definition.Action.Argument | Should Match '-Fixture all -Runs 2 -RequireCriteria'
        $definition.Action.Argument | Should Match ([regex]::Escape("Join-Path 'X:\evidence' (Get-Date -Format 'yyyyMMdd-HHmm')"))
        $definition.Action.Argument | Should Match '-NonInteractive'
    }
}

Describe 'Invoke-Main' {
    Mock Register-ScheduledTask { }
    Mock Register-KitEvalTask { }
    Mock Write-Host { }

    It 'only prints the plan by default (what-if) and registers nothing' {
        $Register = $false
        Invoke-Main | Should Be 0
        Assert-MockCalled Register-ScheduledTask -Times 0 -Exactly
        Assert-MockCalled Register-KitEvalTask -Times 0 -Exactly
        Assert-MockCalled Write-Host -ParameterFilter { $Object -match 'what-if: nothing registered' }
    }
    It 'hands the definition to the registration only with -Register (mocked here)' {
        $Register = $true
        $TaskName = 'Kit Eval Test'
        Invoke-Main | Should Be 0
        Assert-MockCalled Register-KitEvalTask -Times 1 -Exactly -ParameterFilter { $Definition.TaskName -eq 'Kit Eval Test' -and $Definition.LogonType -eq 'Interactive' }
        $Register = $false
    }
}
