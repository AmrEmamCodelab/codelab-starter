# Pester 3.4 parity test for the SPA front site's security edge (Windows PowerShell 5.1). Fails when the IIS template
# deploy/kits/templates/web/web.config or the nginx files deploy/nginx/{security-headers,edge-blocks,trace-context}.conf
# drift from the single source deploy/kits/templates/web/security-headers.psd1 (build plan phase 8 exit criterion):
# the response headers, the blocked paths, and the inbound trace headers stripped before a request reaches a backend.
#   powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester deploy/kits/tests/SecurityHeaders.Parity.Tests.ps1 -EnableExit"
Set-StrictMode -Version Latest

$KitFolder = Split-Path -Parent $PSScriptRoot
$RepoRoot = Split-Path -Parent (Split-Path -Parent $KitFolder)
$HeaderDataPath = Join-Path $KitFolder 'templates\web\security-headers.psd1'
$WebConfigPath = Join-Path $KitFolder 'templates\web\web.config'
$NginxFolder = Join-Path $RepoRoot 'deploy\nginx'
$NginxHeadersPath = Join-Path $NginxFolder 'security-headers.conf'
$NginxBlocksPath = Join-Path $NginxFolder 'edge-blocks.conf'
$NginxExamplePath = Join-Path $NginxFolder 'starter-web.conf.example'
$NginxTraceContextPath = Join-Path $NginxFolder 'trace-context.conf'

# The W3C Trace Context and W3C Baggage request headers (the contract the .psd1 list must keep).
$ExpectedStrippedHeaders = @('traceparent', 'tracestate', 'baggage')
$NoActionType = 'None'
$MatchEveryUrl = '.*'
$TrueText = 'true'
$ListSeparator = ','
$HeaderNameSeparator = '-'
$ServerVariableSeparator = '_'
$SpaFallbackRuleName = 'SPA fallback'
$PathSeparators = [char[]] @('/', '\')
$LocationXPath = '/configuration/location'
$LocationRuleXPathFormat = "system.webServer/rewrite/rules/rule[@name='{0}']"
# proxy_set_header <name> "";
$NginxClearHeaderLine = '^\s*proxy_set_header\s+(?<name>[A-Za-z0-9-]+)\s+""\s*;\s*$'
$NginxTraceIncludePattern = 'include [^;]*/trace-context\.conf;'

$EdgeBlockRuleName = 'Block internal and ops API'
$CustomResponseAction = 'CustomResponse'
# add_header <Name> "<value with \" escapes>" always;
$NginxHeaderLine = '^\s*add_header\s+(?<name>[A-Za-z0-9-]+)\s+"(?<value>(?:[^"\\]|\\.)*)"\s+always;\s*$'
$NginxCommentLine = '^\s*(#.*)?$'
$MinimumHstsSeconds = 31536000

$HeaderData = Import-PowerShellDataFile -LiteralPath $HeaderDataPath
$WebConfig = New-Object Xml.XmlDocument
$WebConfig.Load($WebConfigPath)

function Get-WebConfigHeaders {
    return @($WebConfig.SelectNodes('/configuration/system.webServer/httpProtocol/customHeaders/add') |
        ForEach-Object { [pscustomobject]@{ Name = $_.GetAttribute('name'); Value = $_.GetAttribute('value') } })
}

function ConvertFrom-NginxString([string] $Value) {
    return [regex]::Replace($Value, '\\(.)', '$1')
}

function Get-NginxHeaders {
    $headers = @()
    foreach ($line in [IO.File]::ReadAllLines($NginxHeadersPath)) {
        $match = [regex]::Match($line, $NginxHeaderLine)
        if ($match.Success) { $headers += [pscustomobject]@{ Name = $match.Groups['name'].Value; Value = (ConvertFrom-NginxString $match.Groups['value'].Value) } }
    }
    return $headers
}

function Format-HeaderList([object[]] $Headers) {
    return (@($Headers | ForEach-Object { '{0}: {1}' -f $_.Name, $_.Value }) -join "`n")
}

function Get-SourceHeaderList {
    return @($HeaderData.Headers | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Value = $_.Value } })
}

function Get-CspDirectives([string] $Value) {
    $directives = @{}
    foreach ($directive in $Value.Split(';')) {
        $tokens = @($directive.Trim().Split(' ', [StringSplitOptions]::RemoveEmptyEntries))
        if ($tokens.Count -gt 0) { $directives[$tokens[0]] = @($tokens | Select-Object -Skip 1) }
    }
    return $directives
}

function Get-SourceHeaderValue([string] $Name) {
    return (@($HeaderData.Headers | Where-Object { $_.Name -eq $Name })[0]).Value
}

# traceparent -> HTTP_TRACEPARENT: the IIS server variable that carries a request header.
function ConvertTo-TraceServerVariable([string] $Header) {
    return $HeaderData.TraceStripServerVariablePrefix + $Header.ToUpperInvariant().Replace($HeaderNameSeparator, $ServerVariableSeparator)
}

function Get-ExpectedTraceServerVariables {
    return (@($HeaderData.StrippedRequestHeaders | ForEach-Object { ConvertTo-TraceServerVariable $_ }) -join $ListSeparator)
}

# One entry per <location> of $Document whose rewrite rules hold the trace strip rule, in document order.
function Get-IisTraceStripLocations([Xml.XmlDocument] $Document) {
    $found = @()
    foreach ($location in @($Document.SelectNodes($LocationXPath))) {
        $rule = $location.SelectSingleNode(($LocationRuleXPathFormat -f $HeaderData.TraceStripRuleName))
        if ($null -eq $rule) { continue }
        $sets = @($rule.SelectNodes('serverVariables/set'))
        $found += [pscustomobject]@{
            Path           = $location.GetAttribute('path')
            Variables      = (@($sets | ForEach-Object { $_.GetAttribute('name') }) -join $ListSeparator)
            NonEmptyValues = @($sets | Where-Object { $_.GetAttribute('value') -ne '' }).Count
            MatchUrl       = $rule.SelectSingleNode('match').GetAttribute('url')
            ActionType     = $rule.SelectSingleNode('action').GetAttribute('type')
            StopProcessing = $rule.GetAttribute('stopProcessing')
            RuleCount      = @($location.SelectNodes('system.webServer/rewrite/rules/rule')).Count
        }
    }
    return $found
}

# The header names an nginx file clears with proxy_set_header <name> "";, in order.
function Get-NginxClearedHeaders([string[]] $Lines) {
    return @($Lines | ForEach-Object { [regex]::Match($_, $NginxClearHeaderLine) } | Where-Object { $_.Success } | ForEach-Object { $_.Groups['name'].Value })
}

Describe 'Security headers: one source, two servers' {
    It 'the IIS web.config template carries exactly the source headers, in order' {
        Format-HeaderList (Get-WebConfigHeaders) | Should BeExactly (Format-HeaderList (Get-SourceHeaderList))
    }

    It 'nginx security-headers.conf carries exactly the source headers, in order' {
        Format-HeaderList (Get-NginxHeaders) | Should BeExactly (Format-HeaderList (Get-SourceHeaderList))
    }

    It 'nginx security-headers.conf has nothing but add_header ... always lines and comments' {
        $stray = @([IO.File]::ReadAllLines($NginxHeadersPath) | Where-Object { $_ -notmatch $NginxHeaderLine -and $_ -notmatch $NginxCommentLine })
        ($stray -join "`n") | Should BeExactly ''
    }

    It 'IIS removes X-Powered-By and the Server header' {
        $WebConfig.SelectSingleNode("/configuration/system.webServer/httpProtocol/customHeaders/remove[@name='X-Powered-By']") | Should Not BeNullOrEmpty
        $WebConfig.SelectSingleNode('/configuration/system.webServer/security/requestFiltering').GetAttribute('removeServerHeader') | Should BeExactly 'true'
    }
}

Describe 'Security headers: the values the phase 8 contract requires' {
    $csp = Get-SourceHeaderValue $HeaderData.CspHeaderName
    $directives = Get-CspDirectives $csp

    It 'ships the CSP report-only until the enforce runbook runs' {
        $HeaderData.CspHeaderName | Should BeExactly 'Content-Security-Policy-Report-Only'
        @($HeaderData.Headers | Where-Object { $_.Name -eq $HeaderData.EnforcedCspHeaderName }).Count | Should Be 0
        $csp | Should Not BeNullOrEmpty
    }

    It 'sends CSP reports to the sink through report-uri and report-to' {
        $HeaderData.ReportPath | Should BeExactly '/api/security/csp-reports'
        ($directives['report-uri'] -join ' ') | Should BeExactly $HeaderData.ReportPath
        ($directives['report-to'] -join ' ') | Should BeExactly $HeaderData.ReportGroup
        Get-SourceHeaderValue 'Reporting-Endpoints' | Should BeExactly ('{0}="{1}"' -f $HeaderData.ReportGroup, $HeaderData.ReportPath)
    }

    It 'locks the document down: frame-ancestors, object-src, base-uri, and only hashed inline script' {
        ($directives['frame-ancestors'] -join ' ') | Should BeExactly "'none'"
        ($directives['object-src'] -join ' ') | Should BeExactly "'none'"
        ($directives['base-uri'] -join ' ') | Should BeExactly "'self'"
        ($directives['script-src'] -join ' ') | Should BeExactly ("'self' " + $HeaderData.InlineLoaderScriptHash)
        ($directives['script-src'] -join ' ') | Should Not Match 'unsafe-(inline|eval)'
        ($directives['connect-src'] -join ' ') | Should BeExactly "'self'"
    }

    It 'sets HSTS for a year with subdomains, COOP same-origin, nosniff, a referrer policy, a permissions policy and DENY framing' {
        $hsts = Get-SourceHeaderValue 'Strict-Transport-Security'
        [int] ([regex]::Match($hsts, 'max-age=(\d+)').Groups[1].Value) | Should Not BeLessThan $MinimumHstsSeconds
        $hsts | Should Match 'includeSubDomains'
        Get-SourceHeaderValue 'Cross-Origin-Opener-Policy' | Should BeExactly 'same-origin'
        Get-SourceHeaderValue 'X-Content-Type-Options' | Should BeExactly 'nosniff'
        Get-SourceHeaderValue 'Referrer-Policy' | Should Not BeNullOrEmpty
        Get-SourceHeaderValue 'Permissions-Policy' | Should Match 'camera=\(\)'
        Get-SourceHeaderValue 'X-Frame-Options' | Should BeExactly 'DENY'
    }
}

Describe 'Edge blocks: /api/internal and /api/ops answer 404 at the front site' {
    $rules = @($WebConfig.SelectNodes('/configuration/system.webServer/rewrite/rules/rule'))
    $block = @($rules | Where-Object { $_.GetAttribute('name') -eq $EdgeBlockRuleName })
    $nginxBlocks = [IO.File]::ReadAllText($NginxBlocksPath)

    It 'the source patterns are built from the blocked prefixes' {
        $prefixes = $HeaderData.BlockedPrefixes -join '|'
        $HeaderData.BlockedPathPattern | Should BeExactly ('^/api/+({0})(/|$)' -f $prefixes)
        $HeaderData.BlockedRawPathPattern | Should BeExactly ('^/api/+({0})(/|%2f|$)' -f $prefixes)
        $HeaderData.BlockedStatusCode | Should Be 404
    }

    It 'the patterns block every spelling of the two prefixes and nothing next to them' {
        foreach ($path in @('/api/internal', '/api/internal/gateway/ping', '/API/Ops/health', '/api//ops/jobs', '/api/ops')) {
            [regex]::IsMatch($path, $HeaderData.BlockedPathPattern, 'IgnoreCase') | Should Be $true
        }
        [regex]::IsMatch('/api/ops%2Fhealth', $HeaderData.BlockedRawPathPattern, 'IgnoreCase') | Should Be $true
        foreach ($path in @('/api/opsx', '/api/operations', '/api/internals', '/api/identity/me', '/ops', '/x/api/ops')) {
            [regex]::IsMatch($path, $HeaderData.BlockedPathPattern, 'IgnoreCase') | Should Be $false
        }
    }

    It 'IIS: the block rule is the first rule, matches both patterns case-insensitively and answers 404' {
        $block.Count | Should Be 1
        $rules[0].GetAttribute('name') | Should BeExactly $EdgeBlockRuleName
        $block[0].GetAttribute('stopProcessing') | Should BeExactly 'true'
        $conditions = @($block[0].SelectNodes('conditions/add'))
        $block[0].SelectSingleNode('conditions').GetAttribute('logicalGrouping') | Should BeExactly 'MatchAny'
        (@($conditions | ForEach-Object { '{0} {1} {2}' -f $_.GetAttribute('input'), $_.GetAttribute('pattern'), $_.GetAttribute('ignoreCase') }) -join "`n") |
            Should BeExactly ("{URL} $($HeaderData.BlockedPathPattern) true`n{UNENCODED_URL} $($HeaderData.BlockedRawPathPattern) true")
        $action = $block[0].SelectSingleNode('action')
        $action.GetAttribute('type') | Should BeExactly $CustomResponseAction
        $action.GetAttribute('statusCode') | Should BeExactly ([string] $HeaderData.BlockedStatusCode)
    }

    It 'nginx: edge-blocks.conf returns 404 for the same two patterns' {
        $location = 'location ~\* {0} {{\s*return {1};\s*}}' -f [regex]::Escape($HeaderData.BlockedPathPattern), $HeaderData.BlockedStatusCode
        $raw = 'if \(\$request_uri ~\* "{0}"\) {{\s*return {1};\s*}}' -f [regex]::Escape($HeaderData.BlockedRawPathPattern), $HeaderData.BlockedStatusCode
        $nginxBlocks | Should Match $location
        $nginxBlocks | Should Match $raw
    }

    It 'nginx: the example server includes both files, and every location with its own add_header includes the headers again' {
        $example = [IO.File]::ReadAllText($NginxExamplePath)
        $example | Should Match 'include [^;]*/security-headers\.conf;'
        $example | Should Match 'include [^;]*/edge-blocks\.conf;'
        foreach ($location in [regex]::Matches($example, '(?s)location [^{]*\{(?<body>[^}]*)\}')) {
            $body = $location.Groups['body'].Value
            if ($body -match 'add_header') { $body | Should Match 'include [^;]*/security-headers\.conf;' }
        }
    }
}

Describe 'Inbound trace context: stripped at the front site before a request reaches a backend' {
    $iisLocations = @(Get-IisTraceStripLocations $WebConfig)
    $traceLines = [IO.File]::ReadAllLines($NginxTraceContextPath)
    $expectedVariables = Get-ExpectedTraceServerVariables

    It 'the source strips exactly the W3C trace context and baggage headers' {
        ($HeaderData.StrippedRequestHeaders -join $ListSeparator) | Should BeExactly ($ExpectedStrippedHeaders -join $ListSeparator)
        $HeaderData.TraceStripRuleName | Should Not BeNullOrEmpty
        @($HeaderData.TraceStripPaths).Count | Should BeGreaterThan 0
    }

    It 'names the IIS server variable of a header as URL Rewrite does (HTTP_, upper case, - as _)' {
        ConvertTo-TraceServerVariable 'traceparent' | Should BeExactly 'HTTP_TRACEPARENT'
        ConvertTo-TraceServerVariable 'x-request-id' | Should BeExactly 'HTTP_X_REQUEST_ID'
        $expectedVariables | Should BeExactly 'HTTP_TRACEPARENT,HTTP_TRACESTATE,HTTP_BAGGAGE'
    }

    It 'strips under child application paths (one segment each) that the SPA fallback excludes' {
        $fallback = $WebConfig.SelectSingleNode(("/configuration/system.webServer/rewrite/rules/rule[@name='{0}']" -f $SpaFallbackRuleName))
        $excluded = $fallback.SelectSingleNode("conditions/add[@negate='true' and @input='{URL}']").GetAttribute('pattern')
        foreach ($path in $HeaderData.TraceStripPaths) {
            [regex]::IsMatch("/$path/x", $excluded, 'IgnoreCase') | Should Be $true
            ($path.IndexOfAny($PathSeparators) -lt 0) | Should Be $true
        }
    }

    It 'IIS: exactly one <location> per stripped path carries the rule, in source order' {
        (@($iisLocations | ForEach-Object { $_.Path }) -join $ListSeparator) | Should BeExactly ($HeaderData.TraceStripPaths -join $ListSeparator)
    }

    It 'IIS: each rule sets exactly the stripped headers'' server variables to empty, for every URL, and only sets variables' {
        foreach ($location in $iisLocations) {
            $location.Variables | Should BeExactly $expectedVariables
            $location.NonEmptyValues | Should Be 0
            $location.MatchUrl | Should BeExactly $MatchEveryUrl
            $location.ActionType | Should BeExactly $NoActionType
            $location.StopProcessing | Should Not Be $TrueText
            $location.RuleCount | Should Be 1
        }
    }

    It 'IIS: no site-level rule carries the trace strip rule (it belongs under the backend paths only)' {
        $WebConfig.SelectSingleNode(("/configuration/system.webServer/rewrite/rules/rule[@name='{0}']" -f $HeaderData.TraceStripRuleName)) | Should BeNullOrEmpty
    }

    It 'nginx: trace-context.conf clears exactly the stripped headers, in order, and holds nothing else' {
        ((Get-NginxClearedHeaders $traceLines) -join $ListSeparator) | Should BeExactly ($HeaderData.StrippedRequestHeaders -join $ListSeparator)
        $stray = @($traceLines | Where-Object { $_ -notmatch $NginxClearHeaderLine -and $_ -notmatch $NginxCommentLine })
        ($stray -join "`n") | Should BeExactly ''
    }

    It 'nginx: every proxying location of the example includes trace-context.conf and clears no header by hand' {
        $example = [IO.File]::ReadAllText($NginxExamplePath)
        $proxying = 0
        foreach ($location in [regex]::Matches($example, '(?s)location [^{]*\{(?<body>[^}]*)\}')) {
            $body = $location.Groups['body'].Value
            if ($body -notmatch 'proxy_pass') { continue }
            $proxying++
            $body | Should Match $NginxTraceIncludePattern
        }
        $proxying | Should BeGreaterThan 0
        @(Get-NginxClearedHeaders ($example -split "`n")).Count | Should Be 0
    }

    It 'detects drift: a template or conf missing one header no longer matches the source' {
        $drifted = New-Object Xml.XmlDocument
        $drifted.LoadXml($WebConfig.OuterXml)
        $set = $drifted.SelectSingleNode("/configuration/location/system.webServer/rewrite/rules/rule/serverVariables/set[@name='HTTP_BAGGAGE']")
        [void] $set.ParentNode.RemoveChild($set)
        (@(Get-IisTraceStripLocations $drifted)[0]).Variables | Should Not Be $expectedVariables

        $valued = New-Object Xml.XmlDocument
        $valued.LoadXml($WebConfig.OuterXml)
        $valued.SelectSingleNode("/configuration/location/system.webServer/rewrite/rules/rule/serverVariables/set").SetAttribute('value', 'kept')
        (@(Get-IisTraceStripLocations $valued)[0]).NonEmptyValues | Should Be 1

        $shortConf = @($traceLines | Where-Object { $_ -notmatch 'baggage' })
        ((Get-NginxClearedHeaders $shortConf) -join $ListSeparator) | Should Not Be ($HeaderData.StrippedRequestHeaders -join $ListSeparator)
    }
}
