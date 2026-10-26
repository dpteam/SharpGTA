param(
    [Parameter(Mandatory = $true)]
    [string]$Directory,
    [string]$ExecutableName = "SharpGTA.Headless.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$target = (Resolve-Path $Directory).Path
$executable = Join-Path $target $ExecutableName
if (-not (Test-Path $executable)) { throw "Missing packaged executable: $executable" }

$bad = Get-ChildItem $target -Recurse -File | Where-Object {
    $_.Extension -in @(".img", ".dir", ".dff", ".txd", ".col", ".gxt", ".scm", ".sav", ".asi") -or
    $_.Name -in @("gta3.exe", "gta-vc.exe", "Mss32.dll") -or
    $_.Name -in @("Microsoft.NETFramework.ReferenceAssemblies.dll", "Microsoft.NETFramework.ReferenceAssemblies.net40.dll")
}
if ($bad) { throw "Game assets or unexpected libraries are present in package: $($bad.FullName -join ', ')" }
if (Get-ChildItem $target -Recurse -Directory | Where-Object { $_.Name -ieq "refs" }) {
    throw "Package contains refs/."
}
if (-not (Test-Path (Join-Path $target "SharpGTA.Core.dll"))) { throw "Missing SharpGTA.Core.dll in package." }
if (-not (Test-Path (Join-Path $target "SharpGTA.Games.III.dll"))) { throw "Missing III profile in package." }
if (-not (Test-Path (Join-Path $target "SharpGTA.Games.VC.dll"))) { throw "Missing VC profile in package." }

$version = & $executable --version
if ($LASTEXITCODE -ne 0) { throw "Packaged executable failed to start: $LASTEXITCODE" }
Write-Host $version
Write-Host "Package check passed: $target"
