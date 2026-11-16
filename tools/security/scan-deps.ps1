# Task 051, item 12 — the dependency vulnerability scan (also for CI, task 052).
#   .\tools\security\scan-deps.ps1
# Fails (exit 1) when a NuGet package (direct or transitive) or a production npm package has a known HIGH or
# CRITICAL vulnerability. Uses only the SDKs' own commands — no extra tool is installed. Both need the network for the
# advisory databases (NuGet's vulnerability feed, the npm registry's audit endpoint).
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$failed = $false

Write-Host '== NuGet (dotnet list package --vulnerable --include-transitive)'
Push-Location (Join-Path $root 'src\backend')
try {
    $json = dotnet list Ash.sln package --vulnerable --include-transitive --format json | Out-String
    $report = $json | ConvertFrom-Json
    $found = @()
    foreach ($project in $report.projects) {
        foreach ($framework in @($project.frameworks)) {
            foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                foreach ($v in @($package.vulnerabilities)) {
                    if ($v -and $v.severity -in @('High', 'Critical')) {
                        $found += "$($package.id) $($package.resolvedVersion): $($v.severity) $($v.advisoryurl) ($([IO.Path]::GetFileName($project.path)))"
                    }
                }
            }
        }
    }
    if ($found.Count -gt 0) {
        $failed = $true
        $found | Sort-Object -Unique | ForEach-Object { Write-Host "  HIGH/CRITICAL: $_" -ForegroundColor Red }
    } else {
        Write-Host '  no high or critical vulnerability'
    }
} finally {
    Pop-Location
}

Write-Host '== npm (npm audit --omit=dev --audit-level=high)'
Push-Location (Join-Path $root 'src\frontend')
try {
    npm audit --omit=dev --audit-level=high
    if ($LASTEXITCODE -ne 0) { $failed = $true }
} finally {
    Pop-Location
}

if ($failed) {
    Write-Host 'Dependency scan FAILED: fix or upgrade the packages above.' -ForegroundColor Red
    exit 1
}

Write-Host 'Dependency scan passed.'
exit 0
