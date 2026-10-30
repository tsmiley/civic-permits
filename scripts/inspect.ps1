<#
.SYNOPSIS
    Runs JetBrains InspectCode over the solution and fails when it reports anything at warning level or above.

.DESCRIPTION
    InspectCode is the command-line form of ReSharper's analysis. It finds problems the compiler does not
    (redundant code, possible null dereferences it can prove, naming, unused members). The repository's
    CivicPermits.slnx.DotSettings carries the shared severity settings.

    Install once:  dotnet tool install -g JetBrains.ReSharper.GlobalTools
    Run:           pwsh scripts/inspect.ps1
    Faster check of named files:  pwsh scripts/inspect.ps1 -Include 'src/CivicPermits.Domain/**/*.cs'
#>
[CmdletBinding()]
param(
    [string]$Include = '',
    [string]$Output = (Join-Path ([IO.Path]::GetTempPath()) 'civic-permits-inspect.txt')
)

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

if (-not (Get-Command jb -ErrorAction SilentlyContinue)) {
    throw 'jb (JetBrains.ReSharper.GlobalTools) is not installed. Run: dotnet tool install -g JetBrains.ReSharper.GlobalTools'
}

$arguments = @('inspectcode', 'CivicPermits.slnx', '--severity=WARNING', '--format=Text', "--output=$Output")

if ($Include) {
    $arguments += "--include=$Include"
}

& jb @arguments | Out-Null

# Lines in the text report that name a source file are findings; headers and generator notices are not.
$findings = Get-Content $Output |
    Where-Object { $_ -match '^\s+(src|tests)[\\/].+:\d+ ' }

if ($findings) {
    Write-Host 'InspectCode findings:' -ForegroundColor Red
    $findings | ForEach-Object { Write-Host $_ }
    exit 1
}

Write-Host 'InspectCode: no warnings or errors.' -ForegroundColor Green
