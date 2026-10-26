param(
    [string]$GameRoot = "refs/Grand Theft Auto 3"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$gameRootFull = $null

function Read-SharedText([string]$path) {
    $stream = New-Object IO.FileStream($path, [IO.FileMode]::Open,
        [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        $reader = New-Object IO.StreamReader($stream)
        try { return $reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-WorldLog([string]$name, [string]$text) {
    if (-not $text.Contains("WORLD PREVIEW / FREE FLY (not player gameplay): 8689 static IPL instances.")) {
        throw "$name did not load the expected static IPL world."
    }
    $render = [regex]::Match($text,
        "World-Render: models=(\d+) txd=(\d+) drawCalls=(\d+) textures=(\d+) failedModels=(\d+) missingTextures=(\d+)")
    if (-not $render.Success) {
        throw "$name did not print world render diagnostics."
    }
    [int]$models = $render.Groups[1].Value
    [int]$dictionaries = $render.Groups[2].Value
    [int]$drawCalls = $render.Groups[3].Value
    [int]$textures = $render.Groups[4].Value
    [int]$failedModels = $render.Groups[5].Value
    [int]$missingTextures = $render.Groups[6].Value
    if ($models -le 0 -or $dictionaries -le 1 -or $drawCalls -le 0 -or $textures -le 0) {
        throw "$name did not resolve real world resources: $($render.Value)."
    }
    if ($failedModels -ne 0 -or $text.Contains("World-Render-First-Failure:")) {
        throw "$name has failed model loads: $($render.Value)."
    }
    # This all-IPL preview also draws LODerind02. Its retail material reference is absent
    # from indust1/generic and must not be replaced with a texture from an unrelated TXD.
    if ($missingTextures -ne 2 -or
        -not $text.Contains("World-Render-Missing-Texture: LODerind02: indust1/newall6_16c128")) {
        throw "$name has unexpected texture lookup failures: $($render.Value)."
    }
    Write-Host "$name resources: $($render.Value) (known unresolved LOD reference, not substituted)"
}

function Assert-WorldBmp([string]$name, [string]$path) {
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

    $world = 0
    $bright = 0
    $colors = @{}
    for ($y = 0; $y -lt -$height; $y++) {
        for ($x = 0; $x -lt $width; $x++) {
            $index = $offset + ($y * $width + $x) * 4
            $b = [int]$bytes[$index]
            $g = [int]$bytes[$index + 1]
            $r = [int]$bytes[$index + 2]
            if ($b -eq 0x2E -and $g -eq 0x22 -and $r -eq 0x18) { continue }
            $world++
            if ($r + $g + $b -gt 180) { $bright++ }
            if ($colors.Count -lt 1024) {
                $colors[($r -shl 16) -bor ($g -shl 8) -bor $b] = $true
            }
        }
    }
    if ($world -lt 100000 -or $bright -lt 5000 -or $colors.Count -lt 1000) {
        throw "$name has no sufficiently varied 3D world: world=$world bright=$bright colors=$($colors.Count)."
    }
    Write-Host "$name world pixels=$world bright=$bright colors>=$($colors.Count)"
}

function Invoke-World([string]$name, [string]$command, [string[]]$arguments,
    [string]$screenshot, [switch]$exerciseReset) {
    Write-Host "=== D3D9 world $name ==="
    [string[]]$allArgs = @($arguments) + @(
        "--world", "--game", "iii", "--root", $gameRootFull,
        "--hidden", "--frames", "2", "--screenshot", $screenshot)
    if ($exerciseReset) { $allArgs += "--exercise-reset" }
    [string[]]$lines = @(& $command @allArgs 2>&1 | ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
    $lines | ForEach-Object { Write-Host $_ }
    [IO.File]::WriteAllLines($screenshot + ".log", $lines)
    $text = [string]::Join("`n", $lines)
    if ($exitCode -ne 0) { throw "$name failed with exit code $exitCode." }
    Assert-WorldLog $name $text
    Assert-WorldBmp $name $screenshot
}

function Test-MenuAndInput {
    Write-Host "=== New Game -> world and live input ==="
    $stdout = Join-Path $root "artifacts/world-smoke/menu-input.stdout.log"
    $stderr = Join-Path $root "artifacts/world-smoke/menu-input.stderr.log"
    $exe = Join-Path $root "src/SharpGTA.D3D9/bin/Release/net40/SharpGTA.D3D9.exe"
    $arguments = "--menu --game iii --root `"$gameRootFull`" --frames 100000"
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $root `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    try {
        # Keep the native handle: some .NET Framework Process objects otherwise lose ExitCode.
        $processHandle = $process.Handle
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class SharpGtaWorldSmokeInput
{
    [DllImport("user32.dll", EntryPoint="PostMessageW", CharSet=CharSet.Unicode, ExactSpelling=true)]
    public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
'@
        Start-Sleep -Milliseconds 1200
        $process.Refresh()
        if ($process.HasExited) { throw "Menu-to-world process exited before input." }
        $window = $process.MainWindowHandle
        if ($window -eq [IntPtr]::Zero) { throw "Menu-to-world window was not created." }

        function Send-Key([int]$key) {
            if (-not [SharpGtaWorldSmokeInput]::PostMessage($window, 0x0100, [IntPtr]$key, [IntPtr]1)) {
                throw "Could not post key-down $key."
            }
            Start-Sleep -Milliseconds 180
            if (-not [SharpGtaWorldSmokeInput]::PostMessage($window, 0x0101, [IntPtr]$key, [IntPtr]0xC0000001)) {
                throw "Could not post key-up $key."
            }
            Start-Sleep -Milliseconds 180
        }
        function Send-KeyDown([int]$key) {
            if (-not [SharpGtaWorldSmokeInput]::PostMessage($window, 0x0100, [IntPtr]$key, [IntPtr]1)) {
                throw "Could not post key-down $key."
            }
        }
        function Send-KeyUp([int]$key) {
            if (-not [SharpGtaWorldSmokeInput]::PostMessage($window, 0x0101, [IntPtr]$key, [IntPtr]0xC0000001)) {
                throw "Could not post key-up $key."
            }
        }
        function Wait-CameraAction([string]$action, [int]$minimumCount = 1) {
            $actionDeadline = [DateTime]::UtcNow.AddSeconds(45)
            do {
                Start-Sleep -Milliseconds 100
                $process.Refresh()
                if ($process.HasExited) { throw "World exited while waiting for $action." }
                $currentText = Read-SharedText ($stdout)
                if ([regex]::Matches($currentText, "Camera: action=" +
                    [regex]::Escape($action) + " ").Count -ge $minimumCount) { return }
            } while ([DateTime]::UtcNow -lt $actionDeadline)
            throw "Timed out waiting for actual camera action $action."
        }

        Send-Key 13
        Send-Key 13
        Send-Key 40
        Send-Key 13
        $deadline = [DateTime]::UtcNow.AddSeconds(45)
        $text = ""
        do {
            Start-Sleep -Milliseconds 250
            $process.Refresh()
            if ($process.HasExited) { break }
            if (Test-Path $stdout) { $text = Read-SharedText ($stdout) }
        } while (-not $text.Contains("WORLD PREVIEW / FREE FLY") -and [DateTime]::UtcNow -lt $deadline)
        if (-not $text.Contains("WORLD PREVIEW / FREE FLY")) {
            throw "New Game -> Yes did not reach the static world preview."
        }

        Send-Key 71
        Send-KeyDown 87
        Wait-CameraAction "Move:W"
        Send-KeyUp 87
        foreach ($key in @(65, 83, 68)) {
            Send-KeyDown $key
            Wait-CameraAction ("Move:" + [char]$key)
            Send-KeyUp $key
        }
        Send-KeyDown 81
        Wait-CameraAction "Move:Q"
        Send-KeyUp 81
        Send-KeyDown 69
        Wait-CameraAction "Move:E"
        Send-KeyUp 69
        $rightDown = [IntPtr](100 -bor (100 -shl 16))
        $rightMove = [IntPtr](120 -bor (110 -shl 16))
        foreach ($message in @(
            @(0x0204, [IntPtr]2, $rightDown),
            @(0x0200, [IntPtr]2, $rightMove))) {
            if (-not [SharpGtaWorldSmokeInput]::PostMessage($window, $message[0], $message[1], $message[2])) {
                throw "Could not post RMB message $($message[0])."
            }
        }
        Start-Sleep -Milliseconds 150
        if (-not [SharpGtaWorldSmokeInput]::PostMessage($window, 0x0205, [IntPtr]0, $rightMove)) {
            throw "Could not post RMB up."
        }
        # Ordinary RMB release must not clear a physically held movement key.
        Send-KeyDown 87
        Wait-CameraAction "Move:W"
        foreach ($message in @(
            @(0x0204, [IntPtr]2, $rightDown),
            @(0x0200, [IntPtr]2, $rightMove),
            @(0x0205, [IntPtr]0, $rightMove))) {
            if (-not [SharpGtaWorldSmokeInput]::PostMessage($window, $message[0], $message[1], $message[2])) {
                throw "Could not post held-W RMB message $($message[0])."
            }
        }
        Wait-CameraAction "RMB" 2
        # A mouse-move generated by capture change is valid; wait for the next real W frame.
        Wait-CameraAction "Move:W" 2
        Start-Sleep -Milliseconds 350
        Send-KeyUp 87
        Send-Key 70
        Send-Key 36
        Send-KeyDown 87
        if (-not [SharpGtaWorldSmokeInput]::PostMessage($window, 0x0008, [IntPtr]0, [IntPtr]0)) {
            throw "Could not post focus loss."
        }
        Start-Sleep -Milliseconds 350
        Send-KeyUp 87
        if (-not [SharpGtaWorldSmokeInput]::PostMessage($window, 0x0100, [IntPtr]27, [IntPtr]1)) {
            throw "Could not post Esc."
        }
        if (-not $process.WaitForExit(30000)) { throw "World input probe did not exit after Esc." }
        $process.Refresh()
        if (-not $process.HasExited) { throw "World input probe exit was not observed." }
        $exitCode = $process.ExitCode
        if ($exitCode -ne 0) { throw "World input probe exited with code $exitCode." }
    }
    finally {
        $process.Refresh()
        if (-not $process.HasExited) {
            $process.Kill()
            $process.WaitForExit()
        }
    }

    $text = Read-SharedText ($stdout)
    $errors = Read-SharedText ($stderr)
    if ($errors.Length -ne 0) { throw "World input probe wrote errors: $errors" }
    Assert-WorldLog "menu/input" $text
    if (-not $text.Contains("Ground: model=")) {
        throw "G did not execute a real static COLL ground query."
    }

    function Get-CameraStates([string]$action) {
        return @([regex]::Matches($text,
            "Camera: action=" + [regex]::Escape($action) +
            " position=\(([^,]+),([^,]+),([^\)]+)\) yaw=([^\s]+) pitch=([^\r\n]+)"))
    }
    function Camera-Number($state, [int]$group) {
        return [double]::Parse($state.Groups[$group].Value, [Globalization.CultureInfo]::InvariantCulture)
    }
    $wStates = Get-CameraStates "KeyUp:W"
    $moveW = @(Get-CameraStates "Move:W")
    $firstMoveW = $moveW[0]
    $heldMoveW = $moveW[$moveW.Count - 1]
    $q = @(Get-CameraStates "KeyUp:Q")
    $e = @(Get-CameraStates "KeyUp:E")
    $rmb = @(Get-CameraStates "RMB")
    $focus = @(Get-CameraStates "F")
    $homeStates = @(Get-CameraStates "Home")
    if ($wStates.Count -lt 3 -or $moveW.Count -lt 2 -or $q.Count -ne 1 -or $e.Count -ne 1 -or
        $rmb.Count -lt 2 -or $focus.Count -ne 1 -or $homeStates.Count -ne 1) {
        throw "Camera action diagnostics are incomplete."
    }
    $w = $wStates[0]
    $focusW = $wStates[$wStates.Count - 1]
    $f = $focus[0]
    $h = $homeStates[0]
    $moved = [Math]::Abs((Camera-Number $firstMoveW 1) - (Camera-Number $f 1)) +
        [Math]::Abs((Camera-Number $firstMoveW 2) - (Camera-Number $f 2)) +
        [Math]::Abs((Camera-Number $firstMoveW 3) - (Camera-Number $f 3))
    if ($moved -lt 0.01) { throw "W did not move the free-fly camera." }
    foreach ($action in @("Move:A", "Move:S", "Move:D")) {
        $state = @(Get-CameraStates $action)
        if ($state.Count -ne 1) { throw "$action did not produce actual frame movement." }
    }
    if ((Camera-Number $q[0] 3) -ge (Camera-Number $w 3) -or
        (Camera-Number $e[0] 3) -le (Camera-Number $q[0] 3)) {
        throw "Q/E did not change camera height in the expected directions."
    }
    if ([Math]::Abs((Camera-Number $rmb[$rmb.Count - 1] 4) - (Camera-Number $f 4)) -lt 0.01) {
        throw "RMB drag did not change camera yaw."
    }
    for ($group = 1; $group -le 5; $group++) {
        if ($f.Groups[$group].Value -ne $h.Groups[$group].Value) {
            throw "F and Home did not restore the same camera state."
        }
        if ([Math]::Abs((Camera-Number $focusW $group) - (Camera-Number $h $group)) -gt 0.001) {
            throw "Focus loss did not clear held movement input."
        }
    }
    $heldBefore = $heldMoveW
    $heldRmb = $rmb[$rmb.Count - 1]
    $heldAfter = $wStates[1]
    for ($group = 4; $group -le 5; $group++) {
        if ($heldBefore.Groups[$group].Value -ne $heldRmb.Groups[$group].Value) {
            throw "RMB changed held-W camera orientation before its drag."
        }
    }
    $heldMovement = 0.0
    for ($group = 1; $group -le 3; $group++) {
        $heldMovement += [Math]::Abs((Camera-Number $heldAfter $group) - (Camera-Number $heldRmb $group))
    }
    if ($heldMovement -lt 0.01) {
        throw "Ordinary RMB release incorrectly stopped held W movement."
    }
    if (-not $text.Contains("Camera: action=InputReset")) {
        throw "Focus/capture reset was not delivered to the world viewer."
    }
    Write-Host "PASS New Game -> Yes -> world; verified WASD/QE/RMB, held-W+RMB, F/G/Home/focus-loss/Esc and COLL query."
}

if ($env:OS -ne "Windows_NT") {
    Write-Host "SKIP: real D3D9 world smoke requires Windows."
    return
}

Push-Location $root
try {
    if (-not (Test-Path $GameRoot)) {
        Write-Host "SKIP real world smoke: $GameRoot is absent. NOT RUN, not a pass."
        return
    }
    $gameRootFull = (Resolve-Path $GameRoot).Path
    $project = "src/SharpGTA.D3D9/SharpGTA.D3D9.csproj"
    dotnet build $project -c Release
    if ($LASTEXITCODE -ne 0) { throw "World project build failed." }

    $output = Join-Path $root "artifacts/world-smoke"
    [IO.Directory]::CreateDirectory($output) | Out-Null
    $net40 = Join-Path $root "src/SharpGTA.D3D9/bin/Release/net40/SharpGTA.D3D9.exe"
    $baseline = Join-Path $output "world-no-reset.bmp"
    Invoke-World "net40 baseline" $net40 @() $baseline

    $reset = Join-Path $output "world-reset.bmp"
    Invoke-World "net40 (AnyCPU, Reset)" $net40 @() $reset -exerciseReset
    if ((Get-FileHash $baseline -Algorithm SHA256).Hash -ne (Get-FileHash $reset -Algorithm SHA256).Hash) {
        throw "Managed world resources changed the frame after Reset."
    }
    Write-Host "Reset preserves the world BMP exactly."

    Invoke-World "net9.0-windows" "dotnet" `
        @((Join-Path $root "src/SharpGTA.D3D9/bin/Release/net9.0-windows/SharpGTA.D3D9.dll")) `
        (Join-Path $output "world-net9.bmp") -exerciseReset

    $x86Output = Join-Path $output "x86"
    dotnet build $project -c Release -f net40 -p:PlatformTarget=x86 "-p:OutDir=$x86Output\"
    if ($LASTEXITCODE -ne 0) { throw "World x86 build failed." }
    Invoke-World "net40 (x86)" (Join-Path $x86Output "SharpGTA.D3D9.exe") `
        @() (Join-Path $output "world-x86.bmp") -exerciseReset

    Test-MenuAndInput
    Write-Host "Real world smoke passed: static IPL/DFF/TXD, generic fallback, TXD.IMG, COLL, Reset, menu/input and x86/x64."
}
finally {
    Pop-Location
}
