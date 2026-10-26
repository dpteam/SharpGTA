param(
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if (-not $NoBuild) {
        dotnet build SharpGTA.sln -c Release
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed: $LASTEXITCODE" }
    }

    Write-Host "=== Tests: net9.0 ==="
    dotnet tests/SharpGTA.Tests/bin/Release/net9.0/SharpGTA.Tests.dll
    if ($LASTEXITCODE -ne 0) { throw "net9.0 tests failed: $LASTEXITCODE" }

    Write-Host "=== Tests: net40 ==="
    & tests/SharpGTA.Tests/bin/Release/net40/SharpGTA.Tests.exe
    if ($LASTEXITCODE -ne 0) { throw "net40 tests failed: $LASTEXITCODE" }

    Write-Host "=== Headless repeatability: net40 ==="
    $arguments = @("--game", "iii", "--ticks", "90", "--state-at", "30")
    $first = & src/SharpGTA.Headless/bin/Release/net40/SharpGTA.Headless.exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "First headless run failed: $LASTEXITCODE" }
    $second = & src/SharpGTA.Headless/bin/Release/net40/SharpGTA.Headless.exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "Second headless run failed: $LASTEXITCODE" }
    if (($first -join "`n") -cne ($second -join "`n")) {
        throw "Headless output differs between identical runs."
    }
    $first | Select-Object -Last 4
    Write-Host "Headless output is identical."
}
finally {
    Pop-Location
}
