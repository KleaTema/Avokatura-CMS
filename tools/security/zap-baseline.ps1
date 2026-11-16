# Task 051, item 13 — OWASP ZAP baseline scan (passive) of the local system, authenticated with the dev header.
#
# Runs ONLY against a local Development API (it refuses otherwise: the dev header must never work elsewhere).
# Before: the API on :5004 (Development), then in src/frontend:  npm run build ; npx vite preview --port 4173
# (the built app with the production headers, /api and /hubs proxied to :5004 — same origin, as in production).
#
#   .\tools\security\zap-baseline.ps1                     # as monitor@ash.local (sees every case)
#   .\tools\security\zap-baseline.ps1 -User kryetar@ash.local
#
# The report lands in docs/security/zap-baseline.html (+ .json). Exit code: ZAP's (0 = no warnings above the
# threshold, 1 = failures, 2 = warnings).
param(
    [string]$User = 'monitor@ash.local',
    [string]$Target = 'http://host.docker.internal:4173',
    [string]$Image = 'ghcr.io/zaproxy/zaproxy:stable',
    [int]$Minutes = 5
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')

# the dev header works only in Development: check before sending ZAP anywhere
$config = Invoke-RestMethod -Uri 'http://localhost:5004/api/auth/config'
if ($config.mode -ne 'dev') {
    throw 'The API on :5004 is not in Development (mode is not "dev"): this scan authenticates with the dev header and runs locally only.'
}

$out = Join-Path $root 'docs\security'
New-Item -ItemType Directory -Force -Path $out | Out-Null

# ZAP's replacer adds X-Dev-User to every request it sends (authenticated scan of the pages and the API calls)
$replacer = @(
    '-config replacer.full_list(0).description=devuser',
    '-config replacer.full_list(0).enabled=true',
    '-config replacer.full_list(0).matchtype=REQ_HEADER',
    '-config replacer.full_list(0).matchstr=X-Dev-User',
    '-config replacer.full_list(0).regex=false',
    "-config replacer.full_list(0).replacement=$User"
) -join ' '

docker run --rm `
    --add-host host.docker.internal:host-gateway `
    -v "${out}:/zap/wrk:rw" `
    -t $Image `
    zap-baseline.py -t $Target -m $Minutes -j -a `
    -r zap-baseline.html -J zap-baseline.json `
    -z $replacer
exit $LASTEXITCODE
