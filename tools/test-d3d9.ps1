$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

function Get-Int32([byte[]]$bytes, [int]$offset) {
    return [BitConverter]::ToInt32($bytes, $offset)
}

function Assert-TriangleBmp([string]$name, [string]$path) {
    [byte[]]$bytes = [IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 54 -or [BitConverter]::ToUInt16($bytes, 0) -ne 0x4D42) {
        throw "$name did not produce a BMP file."
    }
    $pixelOffset = Get-Int32 $bytes 10
    $width = Get-Int32 $bytes 18
    $height = Get-Int32 $bytes 22
    if ($width -ne 640 -or $height -ne -480 -or $pixelOffset -ne 54) {
        throw "$name BMP layout is unexpected: offset=$pixelOffset, size=${width}x${height}."
    }
    if ([BitConverter]::ToUInt16($bytes, 28) -ne 32 -or (Get-Int32 $bytes 30) -ne 0) {
        throw "$name BMP is not uncompressed 32-bit BGRA."
    }
    $expectedLength = $pixelOffset + $width * (-$height) * 4
    if ($bytes.Length -ne $expectedLength) {
        throw "$name BMP length is $($bytes.Length), expected $expectedLength."
    }

    function Test-Pixel([int]$x, [int]$y, [string]$kind) {
        $index = $pixelOffset + ($y * $width + $x) * 4
        $blue = [int]$bytes[$index]
        $green = [int]$bytes[$index + 1]
        $red = [int]$bytes[$index + 2]
        switch ($kind) {
            "background" { return $blue -eq 0x40 -and $green -eq 0x20 -and $red -eq 0x20 }
            "red" { return $red -gt 180 -and $green -lt 80 -and $blue -lt 80 }
            "green" { return $green -gt 150 -and $green -gt $red + 50 -and $green -gt $blue + 50 }
            "blue" { return $blue -gt 150 -and $blue -gt $red + 50 -and $blue -gt $green + 50 }
            "inside" { return $blue -ne 0x40 -or $green -ne 0x20 -or $red -ne 0x20 }
            default { throw "Unknown pixel assertion: $kind" }
        }
    }

    if (-not (Test-Pixel 10 10 "background")) { throw "$name background pixel is wrong." }
    if (-not (Test-Pixel 320 130 "red")) { throw "$name red triangle vertex was not rendered." }
    if (-not (Test-Pixel 320 240 "inside")) { throw "$name triangle centre is empty." }
    if (-not (Test-Pixel 430 340 "green")) { throw "$name green triangle vertex was not rendered." }
    if (-not (Test-Pixel 210 340 "blue")) { throw "$name blue triangle vertex was not rendered." }

    $nonBackground = 0
    for ($y = 100; $y -le 380; $y += 4) {
        for ($x = 140; $x -le 500; $x += 4) {
            $index = $pixelOffset + ($y * $width + $x) * 4
            if ([int]$bytes[$index] -ne 0x40 -or
                [int]$bytes[$index + 1] -ne 0x20 -or
                [int]$bytes[$index + 2] -ne 0x20) {
                $nonBackground++
            }
        }
    }
    if ($nonBackground -lt 1000) {
        throw "$name triangle area is too small: $nonBackground samples."
    }
    Write-Host "$name pixel validation passed ($nonBackground non-background samples)."
}

function Invoke-Smoke([string]$name, [string]$command, [string[]]$commandArgs, [string]$screenshot) {
    Write-Host "=== D3D9 $name ==="
    [string[]]$allArgs = @($commandArgs) + @(
        "--hidden", "--frames", "2", "--exercise-reset", "--screenshot", $screenshot)
    & $command @allArgs
    if ($LASTEXITCODE -ne 0) { throw "D3D9 $name smoke failed with exit code $LASTEXITCODE." }
    Assert-TriangleBmp $name $screenshot
}

if ($env:OS -ne "Windows_NT") {
    Write-Host "SKIP: D3D9 smoke requires Windows."
    return
}

Push-Location $root
try {
    $output = Join-Path $root "artifacts/d3d9-smoke"
    [IO.Directory]::CreateDirectory($output) | Out-Null
    $project = "src/SharpGTA.D3D9/SharpGTA.D3D9.csproj"

    dotnet build $project -c Release
    if ($LASTEXITCODE -ne 0) { throw "D3D9 build failed." }

    Invoke-Smoke "net40 (AnyCPU)" `
        (Join-Path $root "src/SharpGTA.D3D9/bin/Release/net40/SharpGTA.D3D9.exe") `
        @() (Join-Path $output "triangle-net40.bmp")
    Invoke-Smoke "net9.0" "dotnet" `
        @((Join-Path $root "src/SharpGTA.D3D9/bin/Release/net9.0-windows/SharpGTA.D3D9.dll")) `
        (Join-Path $output "triangle-net9.bmp")

    $x86Output = Join-Path $output "x86"
    dotnet build $project -c Release -f net40 -p:PlatformTarget=x86 "-p:OutDir=$x86Output\"
    if ($LASTEXITCODE -ne 0) { throw "D3D9 x86 build failed." }
    Invoke-Smoke "net40 (x86)" (Join-Path $x86Output "SharpGTA.D3D9.exe") `
        @() (Join-Path $output "triangle-net40-x86.bmp")

    $testProject = "tests/SharpGTA.Tests/SharpGTA.Tests.csproj"
    $managedX86 = Join-Path $output "managed-x86"
    dotnet build $testProject -c Release -f net40 -p:PlatformTarget=x86 "-p:OutDir=$managedX86\"
    if ($LASTEXITCODE -ne 0) { throw "D3D9 managed x86 test build failed." }
    & (Join-Path $managedX86 "SharpGTA.Tests.exe") D3D9
    if ($LASTEXITCODE -ne 0) { throw "D3D9 managed x86 tests failed." }

    if ([Environment]::Is64BitOperatingSystem) {
        $managedX64 = Join-Path $output "managed-x64"
        dotnet build $testProject -c Release -f net40 -p:PlatformTarget=x64 "-p:OutDir=$managedX64\"
        if ($LASTEXITCODE -ne 0) { throw "D3D9 managed x64 test build failed." }
        & (Join-Path $managedX64 "SharpGTA.Tests.exe") D3D9
        if ($LASTEXITCODE -ne 0) { throw "D3D9 managed x64 tests failed." }
    }
    else {
        Write-Host "SKIP: x64 managed tests require a 64-bit OS."
    }

    Write-Host "D3D9 checks passed: managed x86/x64 ABI tests; net40/net9.0 reset, draw and BMP pixels verified."
}
finally {
    Pop-Location
}
