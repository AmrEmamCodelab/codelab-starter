# The single source of the SPA front site's security headers and edge blocks (build plan phase 8, security edge).
#
# Two files carry these values literally, because neither IIS nor nginx can read this file:
#   deploy/kits/templates/web/web.config   (IIS: customHeaders + the "Block internal and ops API" rewrite rule)
#   deploy/nginx/security-headers.conf     (nginx: add_header ... always;)
#   deploy/nginx/edge-blocks.conf          (nginx: the same blocked paths, return 404)
# deploy/kits/tests/SecurityHeaders.Parity.Tests.ps1 fails when either file drifts from this one. Change a value here
# first, then copy it into both files; Install-Web.ps1 reads this file to check the headers the deployed site serves.
#
# The CSP is report-only until the procedure in docs/runbooks/csp-enforce.md has run on a live tier.
@{
    # The CSP report sink (API, anonymous, 204): application/csp-report (report-uri) and application/reports+json
    # (report-to). Same-origin by default; Install-Web.ps1 makes it absolute when config.json's apiBaseUrl is.
    ReportPath        = '/api/security/csp-reports'
    # The Reporting-Endpoints group the CSP report-to directive names.
    ReportGroup       = 'csp-endpoint'
    # The CSP header name while reporting only; docs/runbooks/csp-enforce.md switches it to EnforcedCspHeaderName.
    CspHeaderName     = 'Content-Security-Policy-Report-Only'
    EnforcedCspHeaderName = 'Content-Security-Policy'

    # API routes the front site never serves (404): /api/internal/* (gateway and service calls) and /api/ops/*
    # (the Ops Console API reaches the API through the API's own binding, never through the front site).
    # Matched case-insensitively against the decoded path, and against the raw path with an encoded slash.
    BlockedPrefixes   = @('internal', 'ops')
    BlockedPathPattern    = '^/api/+(internal|ops)(/|$)'
    BlockedRawPathPattern = '^/api/+(internal|ops)(/|%2f|$)'
    BlockedStatusCode = 404

    # The inline loader Angular's critical-CSS inlining (beasties) writes into index.html. Its text is fixed, so its
    # hash is too; a build that changes it shows up as a script-src report (docs/runbooks/csp-enforce.md).
    InlineLoaderScriptHash = "'sha256-LMY6wYoFV9I4wWzxaq1N/dTpl4iurQktw706UCHK3vM='"

    # The response headers, in the order both files list them. Names are case-insensitive on the wire; the files use
    # exactly these spellings so the parity test can compare them as written.
    Headers = @(
        @{
            Name  = 'Content-Security-Policy-Report-Only'
            Value = "default-src 'self'; script-src 'self' 'sha256-LMY6wYoFV9I4wWzxaq1N/dTpl4iurQktw706UCHK3vM='; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; connect-src 'self'; media-src 'self'; worker-src 'self' blob:; manifest-src 'self'; frame-src 'none'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'; report-uri /api/security/csp-reports; report-to csp-endpoint"
        }
        @{ Name = 'Reporting-Endpoints';          Value = 'csp-endpoint="/api/security/csp-reports"' }
        @{ Name = 'Strict-Transport-Security';    Value = 'max-age=31536000; includeSubDomains' }
        @{ Name = 'Cross-Origin-Opener-Policy';   Value = 'same-origin' }
        @{ Name = 'X-Content-Type-Options';       Value = 'nosniff' }
        @{ Name = 'Referrer-Policy';              Value = 'same-origin' }
        @{ Name = 'Permissions-Policy';           Value = 'accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=(), fullscreen=(self)' }
        # frame-ancestors is not enforced from a report-only header, so clickjacking protection is enforced here.
        @{ Name = 'X-Frame-Options';              Value = 'DENY' }
    )
}
