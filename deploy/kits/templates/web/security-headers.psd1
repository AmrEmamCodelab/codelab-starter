# The single source of the SPA front site's security headers and edge blocks (build plan phase 8, security edge).
#
# Two files carry these values literally, because neither IIS nor nginx can read this file:
#   deploy/kits/templates/web/web.config   (IIS: customHeaders + the "Block internal and ops API" rewrite rule)
#   deploy/nginx/security-headers.conf     (nginx: add_header ... always;)
#   deploy/nginx/edge-blocks.conf          (nginx: the same blocked paths, return 404)
#   deploy/nginx/trace-context.conf        (nginx: the stripped inbound trace headers; in IIS the "Strip inbound trace
#                                           context" rule, one <location> per backend child application)
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

    # Inbound trace context the front site strips before a request reaches a backend: a caller must never choose the
    # trace (or plant baggage) the API logs under. W3C Trace Context and W3C Baggage header names, lowercase as nginx
    # writes them. IIS sets each one's server variable (TraceStripServerVariablePrefix + the name upper-cased, '-' as
    # '_': traceparent -> HTTP_TRACEPARENT) to an empty value in the rule TraceStripRuleName, one <location> per
    # TraceStripPaths entry (the backend child applications of the site, also excluded from the SPA fallback). IIS
    # answers 500.50 under those paths until the server variables are allowed for the site in applicationHost.config
    # (templates/web/README.md). nginx clears them with proxy_set_header <name> "" in deploy/nginx/trace-context.conf,
    # included in every proxying location. Codelab.Observability also ignores an inbound trace context; this is the
    # edge half of that defence.
    StrippedRequestHeaders         = @('traceparent', 'tracestate', 'baggage')
    TraceStripServerVariablePrefix = 'HTTP_'
    TraceStripRuleName             = 'Strip inbound trace context'
    TraceStripPaths                = @('api', 'attachments', 'ops-api')

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
