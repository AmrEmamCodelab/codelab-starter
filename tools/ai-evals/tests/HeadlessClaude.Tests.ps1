# Pester 3.4 tests for tools/ai-evals/lib/HeadlessClaude.psm1 (Windows PowerShell 5.1).
# No claude call: Invoke-LoggedProcess is mocked for Invoke-HeadlessClaude. Workspace tests use small git repositories
# under $TestDrive (which is under %TEMP%) and NodeModules None, so no npm and no network.
#   powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester tools/ai-evals/tests -EnableExit"
Set-StrictMode -Version Latest

$ModulePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'lib\HeadlessClaude.psm1'
Import-Module $ModulePath -Force

$MarkerName = '.codelab-throwaway-workspace.json'

function Write-TestText([string] $Path, [string] $Text) {
    $folder = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $folder)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding($false)))
}

function New-TestFolder([string] $Name) {
    $path = Join-Path $TestDrive ('{0}-{1}' -f $Name, [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $path | Out-Null
    return $path
}

# A small source repository: tracked files (one under src/bin), build output, node_modules, a local secret file and an
# uncommitted change, plus a .gitignore like the starter's.
function New-TestSourceRepository {
    $source = New-TestFolder 'source'
    Write-TestText (Join-Path $source '.gitignore') "bin/`n!packages/tool/src/bin/`nobj/`nnode_modules/`n/artifacts/`nappsettings.*.local.json`n"
    Write-TestText (Join-Path $source 'README.md') "# test repo`n"
    Write-TestText (Join-Path $source 'packages\tool\src\bin\cli.mjs') "export {};`n"
    Write-TestText (Join-Path $source 'app\appsettings.json') "{}`n"
    & git -C $source init -q 2>&1 | Out-Null
    & git -C $source -c user.name=t -c user.email=t@example.invalid add -A 2>&1 | Out-Null
    & git -C $source -c user.name=t -c user.email=t@example.invalid commit -q -m init 2>&1 | Out-Null
    Write-TestText (Join-Path $source 'README.md') "# test repo (changed, not committed)`n"
    Write-TestText (Join-Path $source 'notes\untracked.md') "untracked`n"
    Write-TestText (Join-Path $source 'app\bin\Debug\app.dll') 'binary'
    Write-TestText (Join-Path $source 'app\obj\project.assets.json') '{}'
    Write-TestText (Join-Path $source 'node_modules\left-pad\index.js') 'module.exports = 1;'
    Write-TestText (Join-Path $source 'app\appsettings.Development.local.json') '{"Logging":"local-only"}'
    Write-TestText (Join-Path $source 'certs\dev.pfx') 'not-a-real-key'
    return $source
}

Describe 'ConvertTo-ProcessArgumentString' {
    It 'leaves simple arguments alone and quotes arguments with spaces' {
        ConvertTo-ProcessArgumentString @('build', 'a b') | Should Be 'build "a b"'
    }
    It 'escapes embedded quotes and trailing backslashes' {
        ConvertTo-ProcessArgumentString @('say "hi"') | Should Be '"say \"hi\""'
        ConvertTo-ProcessArgumentString @('C:\dir with space\') | Should Be '"C:\dir with space\\"'
    }
    It 'quotes an empty argument' {
        ConvertTo-ProcessArgumentString @('') | Should Be '""'
    }
    It 'keeps glob-like permission rules as single arguments' {
        ConvertTo-ProcessArgumentString @('Bash(node *)') | Should Be '"Bash(node *)"'
    }
}

Describe 'Get-HeadlessClaudeArguments' {
    It 'asks for print mode, stream-json, verbose output and the turn limit' {
        $arguments = Get-HeadlessClaudeArguments -MaxTurns 12
        $arguments[0] | Should Be '-p'
        ($arguments -join ' ') | Should Match '--output-format stream-json --verbose --max-turns 12'
        ($arguments -join ' ') | Should Match '--permission-mode acceptEdits'
        $arguments -contains '--no-session-persistence' | Should Be $true
        $arguments -contains '--allowedTools' | Should Be $true
    }
    It 'never puts the prompt on the command line' {
        (Get-HeadlessClaudeArguments -MaxTurns 1) -contains 'prompt' | Should Be $false
    }
    It 'always denies the full CI script and pushes' {
        $arguments = Get-HeadlessClaudeArguments -MaxTurns 3 -PermissionMode bypassPermissions
        $arguments -contains '--disallowedTools' | Should Be $true
        $arguments -contains 'Bash(*build.ps1*)' | Should Be $true
    }
    It 'drops the allowlist under bypassPermissions and adds the model when given' {
        $arguments = Get-HeadlessClaudeArguments -MaxTurns 3 -PermissionMode bypassPermissions -Model 'test-model'
        $arguments -contains '--allowedTools' | Should Be $false
        ($arguments -join ' ') | Should Match '--model test-model$'
    }
    It 'allows no any-command call operator, no leading wildcard and no write or environment cmdlet' {
        $arguments = Get-HeadlessClaudeArguments -MaxTurns 3
        $start = [array]::IndexOf($arguments, '--allowedTools') + 1
        $end = [array]::IndexOf($arguments, '--disallowedTools')
        $allowed = @($arguments[$start..($end - 1)])
        $allowed.Count | Should BeGreaterThan 0
        $forbiddenPrefixes = @('PowerShell(& *', 'PowerShell(*', 'Bash(*', 'PowerShell(New-Item', 'PowerShell(Set-',
            'PowerShell(Remove-', 'PowerShell($env:', 'Bash(export', 'Bash(rm', 'PowerShell(Invoke-Expression',
            'PowerShell(Start-Process', 'PowerShell(cmd', 'Bash(cmd')
        foreach ($rule in $allowed) {
            foreach ($prefix in $forbiddenPrefixes) {
                $rule.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) | Should Be $false
            }
        }
    }
    It 'denies deletes, git clean and shell hand-offs in every permission mode' {
        foreach ($mode in @('acceptEdits', 'bypassPermissions')) {
            $arguments = Get-HeadlessClaudeArguments -MaxTurns 3 -PermissionMode $mode
            foreach ($rule in @('PowerShell(Remove-Item *)', 'PowerShell(rm *)', 'PowerShell(del *)', 'Bash(rm *)',
                    'Bash(rmdir *)', 'Bash(git clean*)', 'PowerShell(git clean*)', 'PowerShell(cmd *)',
                    'PowerShell(Invoke-Expression *)', 'PowerShell(Start-Process *)')) {
                $arguments -contains $rule | Should Be $true
            }
        }
    }
    It 'refuses an unknown permission mode and a turn limit below one' {
        { Get-HeadlessClaudeArguments -MaxTurns 3 -PermissionMode 'yolo' } | Should Throw
        { Get-HeadlessClaudeArguments -MaxTurns 0 } | Should Throw
    }
}

Describe 'Get-HeadlessEnvironment' {
    It 'puts node then the private dotnet first on PATH and sets CODELAB_DOTNET and the eval flag' {
        $environment = Get-HeadlessEnvironment -NodeDirectory 'X:\node' -DotnetPath 'X:\dotnet\dotnet.exe' -CurrentPath 'C:\Windows'
        $environment['PATH'] | Should Be 'X:\node;X:\dotnet;C:\Windows'
        $environment['CODELAB_DOTNET'] | Should Be 'X:\dotnet\dotnet.exe'
        $environment = Get-HeadlessEnvironment -NodeDirectory 'X:\node' -DotnetPath 'X:\dotnet.exe' -CurrentPath 'C:\Windows'
        $environment['CODELAB_DOTNET'] | Should Be 'X:\dotnet.exe'
        $environment['CODELAB_EVAL_RUN'] | Should Be '1'
        $environment.ContainsKey('CODELAB_HOOK_LOG') | Should Be $false
    }
    It 'sets the hook log only when one is given' {
        (Get-HeadlessEnvironment -HookLogPath 'X:\hooks.jsonl')['CODELAB_HOOK_LOG'] | Should Be 'X:\hooks.jsonl'
    }
    It 'adds extra variables only when asked (the pipeline-proof approval flag is never a default)' {
        (Get-HeadlessEnvironment).ContainsKey('CODELAB_PIPELINE_PROOF_APPROVAL') | Should Be $false
        $environment = Get-HeadlessEnvironment -ExtraEnvironment @{ CODELAB_PIPELINE_PROOF_APPROVAL = 1 }
        $environment['CODELAB_PIPELINE_PROOF_APPROVAL'] | Should BeExactly '1'
        $environment['CODELAB_EVAL_RUN'] | Should Be '1'
    }
}

Describe 'ConvertTo-SkillName' {
    It 'normalizes slash, plugin prefix, arguments and case' {
        ConvertTo-SkillName ([pscustomobject]@{ skill = '/New-Lookup' }) | Should Be 'new-lookup'
        ConvertTo-SkillName ([pscustomobject]@{ skill = 'kit:new-permission --module Audit' }) | Should Be 'new-permission'
        ConvertTo-SkillName ([pscustomobject]@{ command = 'build-feature' }) | Should Be 'build-feature'
        ConvertTo-SkillName ([pscustomobject]@{ other = 'x' }) | Should Be $null
    }
}

Describe 'Get-HeadlessTranscriptSummary' {
    $transcript = Join-Path $TestDrive 'transcript.jsonl'
    $lines = @(
        '{"type":"system","subtype":"init","tools":["Skill"]}',
        '{"type":"assistant","message":{"content":[{"type":"text","text":"routing"},{"type":"tool_use","name":"Skill","input":{"skill":"new-lookup"}}]}}',
        '{"type":"user","message":{"content":[{"type":"tool_result","content":"ok"}]}}',
        '{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Bash","input":{"command":"node tools/scaffold/new-lookup.mjs"}}]}}',
        '{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Skill","input":{"skill":"/coding-standards"}},{"type":"tool_use","name":"Skill","input":{"skill":"new-lookup"}}]}}',
        'not json but mentions "tool_use"',
        '{"type":"result","subtype":"success","is_error":false,"num_turns":7,"total_cost_usd":0.5,"permission_denials":[{"tool_name":"Bash"}],"result":"Done."}'
    )
    Write-TestText $transcript ($lines -join "`n")

    It 'lists the skills invoked through the Skill tool, distinct and in call order' {
        $summary = Get-HeadlessTranscriptSummary -TranscriptPath $transcript
        @($summary.SkillsInvoked) -join ',' | Should Be 'new-lookup,coding-standards'
    }
    It 'counts tool uses, unparsed lines and permission denials and reads the result event' {
        $summary = Get-HeadlessTranscriptSummary -TranscriptPath $transcript
        $summary.ToolUseCounts['Skill'] | Should Be 3
        $summary.ToolUseCounts['Bash'] | Should Be 1
        $summary.UnparsedLines | Should Be 1
        $summary.HasResult | Should Be $true
        $summary.ResultSubtype | Should Be 'success'
        $summary.NumTurns | Should Be 7
        $summary.PermissionDenials | Should Be 1
        $summary.ResultText | Should Be 'Done.'
    }
    It 'returns an empty summary for a missing transcript' {
        $summary = Get-HeadlessTranscriptSummary -TranscriptPath (Join-Path $TestDrive 'missing.jsonl')
        @($summary.SkillsInvoked).Count | Should Be 0
        $summary.HasResult | Should Be $false
    }
}

Describe 'Invoke-HeadlessClaude (process mocked)' {
    $workRoot = New-TestFolder 'work'
    $transcript = Join-Path $TestDrive 'run\transcript.jsonl'
    $hookLog = Join-Path $TestDrive 'run\hooks\hook-log.jsonl'
    Mock -ModuleName HeadlessClaude Invoke-LoggedProcess {
        $line = '{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Skill","input":{"skill":"new-permission"}}]}}'
        [IO.File]::WriteAllText($LogPath, $line + "`n" + '{"type":"result","subtype":"success","is_error":false}')
        return [pscustomobject]@{ ExitCode = 0; TimedOut = $false; DurationSeconds = 4.2; LogPath = $LogPath; ErrorLogPath = $LogPath + '.stderr.log' }
    }

    $result = Invoke-HeadlessClaude -WorkRoot $workRoot -Prompt 'Add an access right.' -TranscriptPath $transcript -MaxTurns 5 `
        -TimeoutMinutes 2 -HookLogPath $hookLog -ClaudePath 'X:\claude.exe' -NodeDirectory 'X:\node' -DotnetPath 'X:\dotnet.exe'

    It 'returns the contract shape parsed from the transcript' {
        $result.ExitCode | Should Be 0
        $result.TranscriptPath | Should Be $transcript
        $result.DurationSeconds | Should Be 4.2
        @($result.SkillsInvoked) -join ',' | Should Be 'new-permission'
        $result.TimedOut | Should Be $false
    }
    It 'sends the prompt on stdin, runs in the work root and passes the hook log and dotnet to the child' {
        Assert-MockCalled -ModuleName HeadlessClaude Invoke-LoggedProcess -Times 1 -Exactly -ParameterFilter {
            $FilePath -eq 'X:\claude.exe' -and $StandardInputText -eq 'Add an access right.' -and $WorkingDirectory -eq $workRoot -and
            $Environment['CODELAB_HOOK_LOG'] -eq $hookLog -and $Environment['CODELAB_DOTNET'] -eq 'X:\dotnet.exe' -and
            ($ArgumentList -join ' ') -match '--max-turns 5' -and $TimeoutMinutes -eq 2
        }
    }
    It 'creates the hook log folder' {
        Test-Path -LiteralPath (Split-Path -Parent $hookLog) | Should Be $true
    }
    It 'refuses a missing work root' {
        { Invoke-HeadlessClaude -WorkRoot (Join-Path $TestDrive 'nope') -Prompt 'x' -TranscriptPath $transcript -MaxTurns 1 -TimeoutMinutes 1 } | Should Throw
    }
}

Describe 'Invoke-LoggedProcess (real process)' {
    It 'captures stdout, feeds stdin and passes child-only environment variables' {
        $log = Join-Path $TestDrive 'proc\out.log'
        $run = Invoke-LoggedProcess -FilePath 'cmd.exe' -ArgumentList @('/d', '/c', 'echo %EVAL_TEST_VALUE%') -WorkingDirectory $TestDrive `
            -LogPath $log -TimeoutMinutes 1 -Environment @{ EVAL_TEST_VALUE = 'child-only' }
        $run.ExitCode | Should Be 0
        $run.TimedOut | Should Be $false
        (Get-Content -LiteralPath $log -Raw) | Should Match 'child-only'
        [Environment]::GetEnvironmentVariable('EVAL_TEST_VALUE') | Should Be $null
    }
    It 'returns the exit code of a failing process' {
        $run = Invoke-LoggedProcess -FilePath 'cmd.exe' -ArgumentList @('/d', '/c', 'exit 3') -WorkingDirectory $TestDrive `
            -LogPath (Join-Path $TestDrive 'proc\fail.log') -TimeoutMinutes 1
        $run.ExitCode | Should Be 3
    }
}

Describe 'Workspace marker safety' {
    It 'refuses a folder without a marker, and Remove-ThrowawayWorkspace leaves it intact' {
        $folder = New-TestFolder 'plain'
        Write-TestText (Join-Path $folder 'keep.txt') 'keep'
        Get-WorkspaceRemovalRefusal -Path $folder -AllowedRoot $TestDrive | Should Match 'no workspace marker'
        { Remove-ThrowawayWorkspace -Path $folder -AllowedRoot $TestDrive } | Should Throw
        Test-Path -LiteralPath (Join-Path $folder 'keep.txt') | Should Be $true
    }
    It 'refuses a foreign or unreadable marker' {
        $foreign = New-TestFolder 'foreign'
        Write-TestText (Join-Path $foreign $MarkerName) (@{ tool = 'other'; path = $foreign } | ConvertTo-Json)
        Get-WorkspaceRemovalRefusal -Path $foreign -AllowedRoot $TestDrive | Should Match 'foreign'
        $broken = New-TestFolder 'broken'
        Write-TestText (Join-Path $broken $MarkerName) '{ not json'
        Get-WorkspaceRemovalRefusal -Path $broken -AllowedRoot $TestDrive | Should Match 'unreadable'
    }
    It 'refuses a marker copied from another workspace (it names another path)' {
        $copy = New-TestFolder 'copied'
        Write-TestText (Join-Path $copy $MarkerName) (@{ tool = 'codelab-ai-evals'; path = (Join-Path $TestDrive 'elsewhere') } | ConvertTo-Json)
        Get-WorkspaceRemovalRefusal -Path $copy -AllowedRoot $TestDrive | Should Match 'names another path'
    }
    It 'refuses paths outside the allowed root, the root itself, a drive root and a missing folder' {
        Get-WorkspaceRemovalRefusal -Path 'C:\Windows' -AllowedRoot $TestDrive | Should Match 'not below the allowed root'
        Get-WorkspaceRemovalRefusal -Path $TestDrive -AllowedRoot $TestDrive | Should Match 'not below the allowed root'
        Get-WorkspaceRemovalRefusal -Path 'C:\' -AllowedRoot 'C:\' | Should Match 'drive root'
        Get-WorkspaceRemovalRefusal -Path (Join-Path $TestDrive 'missing') -AllowedRoot $TestDrive | Should Match 'does not exist'
    }
    It 'does not treat a sibling folder with a shared prefix as inside the root' {
        Test-PathIsUnder -Path 'C:\temp-other\x' -Root 'C:\temp' | Should Be $false
        Test-PathIsUnder -Path 'C:\temp\x' -Root 'C:\temp\' | Should Be $true
    }
}

Describe 'New-ThrowawayWorkspace and Remove-ThrowawayWorkspace' {
    $source = New-TestSourceRepository
    $destination = Join-Path $TestDrive 'workspaces\ws-one'
    $workspace = New-ThrowawayWorkspace -Source $source -Destination $destination -NodeModules None -AllowedRoot $TestDrive

    It 'copies the working tree including uncommitted and untracked changes' {
        (Get-Content -LiteralPath (Join-Path $destination 'README.md') -Raw) | Should Match 'not committed'
        Test-Path -LiteralPath (Join-Path $destination 'notes\untracked.md') | Should Be $true
    }
    It 'skips build output, installed packages and key material, but restores tracked files under excluded names' {
        Test-Path -LiteralPath (Join-Path $destination 'app\bin') | Should Be $false
        Test-Path -LiteralPath (Join-Path $destination 'app\obj') | Should Be $false
        Test-Path -LiteralPath (Join-Path $destination 'node_modules') | Should Be $false
        Test-Path -LiteralPath (Join-Path $destination 'app\appsettings.Development.local.json') | Should Be $false
        Test-Path -LiteralPath (Join-Path $destination 'certs\dev.pfx') | Should Be $false
        Test-Path -LiteralPath (Join-Path $destination 'packages\tool\src\bin\cli.mjs') | Should Be $true
        $workspace.RestoredFiles | Should Be 1
    }
    It 'writes a marker for this path and keeps it out of git' {
        $marker = Get-Content -LiteralPath (Join-Path $destination $MarkerName) -Raw | ConvertFrom-Json
        $marker.tool | Should Be 'codelab-ai-evals'
        $marker.path | Should Be $destination
        $workspace.MarkerId | Should Be $marker.id
        Test-ThrowawayWorkspace -Path $destination -AllowedRoot $TestDrive | Should Be $true
    }
    It 'makes one snapshot commit tagged eval-base with a clean tree, so the docs gate has a base ref' {
        $workspace.BaseRef | Should Be 'eval-base'
        @(& git -C $destination rev-list --count eval-base) | Should Be '1'
        @(& git -C $destination status --porcelain).Count | Should Be 0
        @(& git -C $destination remote).Count | Should Be 0
    }
    It 'refuses a destination that is not empty, inside the source, or outside the allowed root' {
        { New-ThrowawayWorkspace -Source $source -Destination $destination -NodeModules None -AllowedRoot $TestDrive } | Should Throw
        { New-ThrowawayWorkspace -Source $source -Destination (Join-Path $source 'ws') -NodeModules None -AllowedRoot $TestDrive } | Should Throw
        { New-ThrowawayWorkspace -Source $source -Destination 'C:\eval-ws-outside' -NodeModules None -AllowedRoot $TestDrive } | Should Throw
        Test-Path -LiteralPath 'C:\eval-ws-outside' | Should Be $false
    }
    It 'removes the workspace without following a junction out of it' {
        $outside = New-TestFolder 'outside'
        Write-TestText (Join-Path $outside 'precious.txt') 'keep me'
        & cmd.exe /d /c mklink /J "`"$(Join-Path $destination 'linked')`"" "`"$outside`"" 2>&1 | Out-Null
        Test-Path -LiteralPath (Join-Path $destination 'linked\precious.txt') | Should Be $true
        Remove-ThrowawayWorkspace -Path $destination -AllowedRoot $TestDrive
        Test-Path -LiteralPath $destination | Should Be $false
        Test-Path -LiteralPath (Join-Path $outside 'precious.txt') | Should Be $true
    }
}
