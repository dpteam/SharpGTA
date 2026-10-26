param(
    [string]$GameRoot = "refs/Grand Theft Auto 3"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

function Assert-ModelBmp([string]$name, [string]$path) {
    [byte[]]$bytes = [IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 54 -or [BitConverter]::ToUInt16($bytes, 0) -ne 0x4D42) {
        throw "$name did not produce a BMP."
    }
    $offset = [BitConverter]::ToInt32($bytes, 10)
    $width = [BitConverter]::ToInt32($bytes, 18)
    $height = [BitConverter]::ToInt32($bytes, 22)
    if ($width -ne 640 -or $height -ne -480) {
        throw "$name has unexpected dimensions: ${width}x${height}."
    }
    if ($bytes.Length -ne $offset + $width * (-$height) * 4) {
        throw "$name has an invalid BMP payload length."
    }

    $background = 0
    $model = 0
    $bright = 0
    $minX = $width
    $minY = -$height
    $maxX = -1
    $maxY = -1
    for ($y = 0; $y -lt -$height; $y++) {
        for ($x = 0; $x -lt $width; $x++) {
            $index = $offset + ($y * $width + $x) * 4
            $b = [int]$bytes[$index]
            $g = [int]$bytes[$index + 1]
            $r = [int]$bytes[$index + 2]
            if ($b -eq 0x2E -and $g -eq 0x22 -and $r -eq 0x18) {
                $background++
                continue
            }
            $model++
            $minX = [Math]::Min($minX, $x)
            $minY = [Math]::Min($minY, $y)
            $maxX = [Math]::Max($maxX, $x)
            $maxY = [Math]::Max($maxY, $y)
            if ($r + $g + $b -gt 180) { $bright++ }
        }
    }
    if ($model -lt 5000 -or $bright -lt 500 -or $maxX -le $minX -or $maxY -le $minY) {
        throw "$name has no sufficiently lit 3D model: model=$model bright=$bright bounds=$minX,$minY..$maxX,$maxY."
    }
    if ($background -lt 250000) {
        throw "$name background is not preserved."
    }
    Write-Host "$name model pixels=$model bright=$bright bounds=$minX,$minY..$maxX,$maxY"
}

function Assert-ModelLog([string]$name, [string]$text) {
    if (-not $text.Contains("Model: player atomics=16 drawCalls=16 triangles=1550 textures=1 missing=0")) {
        throw "$name did not resolve the original PLAYER.DFF/TXD without substitutions."
    }
}

function Invoke-Model([string]$name, [string]$command, [string[]]$arguments, [string]$screenshot) {
    Write-Host "=== D3D9 model $name ==="
    [string[]]$allArgs = @($arguments) + @(
        "--model", "player", "--game", "iii", "--root", $GameRoot,
        "--hidden", "--frames", "2", "--exercise-reset", "--screenshot", $screenshot)
    [string[]]$lines = @(& $command @allArgs 2>&1 | ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
    $lines | ForEach-Object { Write-Host $_ }
    [IO.File]::WriteAllLines($screenshot + ".log", $lines)
    if ($exitCode -ne 0) { throw "$name failed with exit code $exitCode." }
    Assert-ModelLog $name ([string]::Join("`n", $lines))
    Assert-ModelBmp $name $screenshot
}

if ($env:OS -ne "Windows_NT") {
    Write-Host "SKIP: real D3D9 model smoke requires Windows."
    return
}

Push-Location $root
try {
    if (-not (Test-Path $GameRoot)) {
        Write-Host "SKIP real model smoke: $GameRoot is absent. NOT RUN, not a pass."
        return
    }
    $project = "src/SharpGTA.D3D9/SharpGTA.D3D9.csproj"
    dotnet build $project -c Release
    if ($LASTEXITCODE -ne 0) { throw "Model project build failed." }

    $output = Join-Path $root "artifacts/model-smoke"
    [IO.Directory]::CreateDirectory($output) | Out-Null
    $net40 = Join-Path $root "src/SharpGTA.D3D9/bin/Release/net40/SharpGTA.D3D9.exe"
    $baseline = Join-Path $output "player-no-reset.bmp"
    [string[]]$baselineLines = @(& $net40 --model player --game iii --root $GameRoot --hidden --frames 2 --screenshot $baseline 2>&1 | ForEach-Object { $_.ToString() })
    $baselineExitCode = $LASTEXITCODE
    $baselineLines | ForEach-Object { Write-Host $_ }
    if ($baselineExitCode -ne 0) { throw "Baseline model smoke failed: $baselineExitCode" }
    Assert-ModelLog "net40 baseline" ([string]::Join("`n", $baselineLines))
    Assert-ModelBmp "net40 baseline" $baseline

    $reset = Join-Path $output "player-reset.bmp"
    Invoke-Model "net40 (AnyCPU, Reset)" $net40 @() $reset
    $baselineHash = (Get-FileHash $baseline -Algorithm SHA256).Hash
    $resetHash = (Get-FileHash $reset -Algorithm SHA256).Hash
    if ($baselineHash -ne $resetHash) {
        throw "Managed model textures changed the frame after Reset."
    }
    Write-Host "Reset preserves the model BMP exactly."

    Invoke-Model "net9.0-windows" "dotnet" `
        @((Join-Path $root "src/SharpGTA.D3D9/bin/Release/net9.0-windows/SharpGTA.D3D9.dll")) `
        (Join-Path $output "player-net9.bmp")

    $x86Output = Join-Path $output "x86"
    dotnet build $project -c Release -f net40 -p:PlatformTarget=x86 "-p:OutDir=$x86Output\"
    if ($LASTEXITCODE -ne 0) { throw "Model x86 build failed." }
    Invoke-Model "net40 (x86)" (Join-Path $x86Output "SharpGTA.D3D9.exe") `
        @() (Join-Path $output "player-x86.bmp")

    Write-Host "Real model smoke passed: original PLAYER.DFF/TXD, hierarchy, light, depth, reset and x86/x64."
}
finally {
    Pop-Location
}
