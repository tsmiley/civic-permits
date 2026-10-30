<#
.SYNOPSIS
    Fails when any restored package, direct or transitive, is not MIT, Apache-2.0 or BSD licensed.

.DESCRIPTION
    Reads every project's obj/project.assets.json after a restore, finds each package's .nuspec in the
    NuGet cache and checks its SPDX license expression against an allow list. Packages that publish only
    a license URL (an older convention) are listed and must be added to the reviewed table below by hand,
    so nothing passes because the audit could not read it.

    Run after `dotnet restore`:  pwsh scripts/license-audit.ps1
#>
[CmdletBinding()]
param(
    [string]$Root = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'

$allowed = @('MIT', 'Apache-2.0', 'BSD-2-Clause', 'BSD-3-Clause', '0BSD')

# Packages whose nuspec carries no SPDX expression, reviewed by hand against the license file inside the
# package. These are NOT MIT, Apache-2.0 or BSD. They are listed so the exception is visible and agreed, and
# the audit fails on anything else it cannot classify.
#
# Both are Microsoft redistributable runtime components that arrive through Microsoft.Data.SqlClient, which the
# SQL Server provider requires. The repository does not contain them: NuGet restores them from nuget.org, and
# they are covered by Microsoft's own license terms (documented in NOTICE). Accepted 2026-10-30 by Fable-Extra
# (redistributable runtime components of a required provider, not stored in the repository).
#
# Deque.AxeCore.Commons carries the axe-core accessibility engine, which is MPL-2.0 (a file-level, weak copyleft
# license). It is used unmodified, for tests only, and is never part of the built application. Allowed 2026-10-30 by
# Fable-Extra (test-only dependency, MPL-2.0 file-level copyleft does not reach our code), on these conditions: its
# files are never edited, and NOTICE names it as MPL-2.0 (never MIT).
$reviewed = @{
    'Microsoft.Data.SqlClient.SNI.runtime'  = 'Microsoft Software License Terms (proprietary redistributable, native SQL networking library)'
    'Microsoft.Identity.Client.NativeInterop' = 'Microsoft Software License Terms (proprietary redistributable, native authentication interop)'
    'Deque.AxeCore.Commons'                 = 'MPL-2.0 (axe-core accessibility engine, unmodified, tests only)'
}

$packagesFolder = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME '.nuget/packages' }
$assetFiles = Get-ChildItem -Path $Root -Recurse -Filter project.assets.json -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '[\\/]obj[\\/]' }

if (-not $assetFiles) {
    throw 'No project.assets.json found. Run dotnet restore first.'
}

$seen = @{}
foreach ($file in $assetFiles) {
    $assets = Get-Content $file.FullName -Raw | ConvertFrom-Json
    foreach ($name in $assets.libraries.PSObject.Properties.Name) {
        if ($assets.libraries.$name.type -ne 'package') { continue }
        $seen[$name] = $true
    }
}

$failures = @()
$rows = foreach ($key in ($seen.Keys | Sort-Object)) {
    $id, $version = $key -split '/', 2
    $nuspec = Join-Path $packagesFolder "$($id.ToLowerInvariant())/$version/$($id.ToLowerInvariant()).nuspec"

    if (-not (Test-Path $nuspec)) {
        $failures += "$key : nuspec not found in $packagesFolder"
        [pscustomobject]@{ Package = $id; Version = $version; License = '?'; Verdict = 'UNREADABLE' }
        continue
    }

    [xml]$xml = Get-Content $nuspec -Raw
    $license = $xml.package.metadata.license
    $expression = if ($license -and $license.type -eq 'expression') { $license.'#text' } else { $null }

    if ($expression) {
        $parts = $expression -split '\s+(?:OR|AND)\s+' | ForEach-Object { $_.Trim('(', ')', ' ') }
        $ok = ($parts | Where-Object { $allowed -contains $_ }).Count -eq $parts.Count -or
              ($expression -match '\bOR\b' -and ($parts | Where-Object { $allowed -contains $_ }).Count -ge 1)
        $verdict = if ($ok) { 'ok' } elseif ($reviewed.ContainsKey($id) -and $expression -eq 'MPL-2.0') { 'EXCEPTION (reviewed, see NOTICE)' } else { 'NOT ALLOWED' }
    }
    elseif ($reviewed.ContainsKey($id)) {
        $expression = $reviewed[$id]
        $verdict = 'EXCEPTION (reviewed, see NOTICE)'
    }
    else {
        $expression = "no SPDX expression; licenseUrl=$($xml.package.metadata.licenseUrl)"
        $verdict = 'NEEDS REVIEW'
    }

    if ($verdict -in 'NOT ALLOWED', 'NEEDS REVIEW') {
        $failures += "$key : $expression"
    }

    [pscustomobject]@{ Package = $id; Version = $version; License = $expression; Verdict = $verdict }
}

$rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
Write-Host "$($rows.Count) packages audited."

if ($failures) {
    Write-Host 'License audit FAILED:' -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}

$exceptions = @($rows | Where-Object { $_.Verdict -like 'EXCEPTION*' })
Write-Host "License audit passed: $($rows.Count - $exceptions.Count) packages are MIT, Apache-2.0 or BSD; $($exceptions.Count) are reviewed exceptions listed in NOTICE." -ForegroundColor Green
