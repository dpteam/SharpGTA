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
    if (-not $text.Contains("backend=indexed-managed")) {
        throw "$name did not use managed vertex/index buffers."
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
    # LOD handoff may skip the known LODerind02 replacement near the camera.
    # If its material is visible, it must remain the same explicit unresolved reference.
    if ($missingTextures -ne 0 -and
        ($missingTextures -ne 2 -or
         -not $text.Contains("World-Render-Missing-Texture: LODerind02: indust1/newall6_16c128"))) {
        throw "$name has unexpected texture lookup failures: $($render.Value)."
    }
    $sectors = [regex]::Match($text,
        "World-Sectors: candidates=(\d+) instances=(\d+) unindexed=(\d+)")
    if (-not $sectors.Success) {
        throw "$name did not print sector diagnostics."
    }
    [int]$candidates = $sectors.Groups[1].Value
    [int]$allInstances = $sectors.Groups[2].Value
    if ($candidates -le 0 -or $candidates -ge $allInstances) {
        throw "$name sector query did not narrow the instance scan: $($sectors.Value)."
    }
    $verify = [regex]::Match($text,
        "World-Sectors-Verify: sectors=(\d+) linear=(\d+) missing=(\d+) extra=(\d+)")
    if (-not $verify.Success) {
        throw "$name did not verify the sector index."
    }
    if ([int]$verify.Groups[1].Value -ne [int]$verify.Groups[2].Value -or
        [int]$verify.Groups[3].Value -ne 0 -or [int]$verify.Groups[4].Value -ne 0) {
        throw "$name sector index disagrees with a linear scan: $($verify.Value)."
    }
    Write-Host "$name sectors: $($sectors.Value) (verified against linear scan)"

    $entities = [regex]::Match($text,
        "World-Entities: buildings=(\d+)/(\d+) dummies=(\d+)/(\d+) treadables=(\d+)/(\d+) objects=(\d+)/(\d+) rejected=(\d+)")
    if (-not $entities.Success) {
        throw "$name did not print entity-pool diagnostics."
    }
    [int]$placementTotal = 0
    for ($group = 1; $group -le 7; $group += 2) {
        $used = [int]$entities.Groups[$group].Value
        $capacity = [int]$entities.Groups[$group + 1].Value
        if ($used -gt $capacity) {
            throw "$name overflowed an entity pool: $($entities.Value)."
        }
        # Buildings, dummies and treadables come from the map; objects are spawned.
        if ($group -le 5) { $placementTotal += $used }
    }
    if ([int]$entities.Groups[9].Value -ne 0) {
        throw "$name left placements without a pool handle: $($entities.Value)."
    }
    if ($placementTotal -ne $allInstances) {
        throw "$name placement pools do not cover every instance: $($entities.Value)."
    }
    if ($name -ne "spawn" -and [int]$entities.Groups[7].Value -ne 0) {
        throw "$name spawned gameplay objects without asking: $($entities.Value)."
    }
    Write-Host "$name entities: $($entities.Value)"

    $stream = [regex]::Match($text,
        "World-Streaming: models=(\d+)/(\d+) txds=(\d+)/(\d+) evictedModels=(\d+) evictedTxds=(\d+) remaining=(\d+) deferred=(\d+)")
    if (-not $stream.Success) {
        throw "$name did not print streaming diagnostics."
    }
    [int]$streamModels = $stream.Groups[1].Value
    [int]$modelCapacity = $stream.Groups[2].Value
    [int]$streamTxds = $stream.Groups[3].Value
    [int]$remaining = $stream.Groups[7].Value
    if ($streamModels -le 0 -or $streamTxds -le 0 -or
        $streamModels -gt $modelCapacity -or $remaining -ne 0) {
        throw "$name has invalid streaming state: $($stream.Value)."
    }
    $bytes = [regex]::Match($text,
        "World-Bytes: cached=(\d+) bytes=(\d+)/(\d+) pending=(\d+) evicted=(\d+) failed=(\d+)")
    if (-not $bytes.Success) {
        throw "$name did not print async byte diagnostics."
    }
    if ([int]$bytes.Groups[2].Value -gt [int]$bytes.Groups[3].Value) {
        throw "$name exceeded its async byte budget: $($bytes.Value)."
    }
    if ([int]$bytes.Groups[6].Value -ne 0) {
        throw "$name has failed async byte reads: $($bytes.Value)."
    }
    Write-Host "$name streaming: $($stream.Value)"
    Write-Host "$name bytes: $($bytes.Value)"
}

function Assert-WorldFinal([string]$name, [string]$text) {
    $final = [regex]::Match($text,
        "World-Streaming-Final: models=(\d+)/\d+ txds=\d+/\d+ evictedModels=\d+ evictedTxds=\d+ remaining=(\d+) deferred=(\d+)")
    if (-not $final.Success) {
        throw "$name did not print final streaming diagnostics."
    }
    if ([int]$final.Groups[2].Value -ne 0 -or [int]$final.Groups[3].Value -ne 0) {
        throw "$name ended with queued or deferred streaming work: $($final.Value)."
    }
    $bytesFinal = [regex]::Match($text,
        "World-Bytes-Final: cached=\d+ bytes=(\d+)/(\d+) pending=\d+ evicted=\d+ failed=(\d+) oversize=(\d+)")
    if (-not $bytesFinal.Success) {
        throw "$name did not print final async byte diagnostics."
    }
    if ([int]$bytesFinal.Groups[3].Value -ne 0) {
        throw "$name ended with failed async byte reads: $($bytesFinal.Value)."
    }
    if ([long]$bytesFinal.Groups[1].Value -gt [long]$bytesFinal.Groups[2].Value) {
        throw "$name ended outside its async byte budget: $($bytesFinal.Value)."
    }
    Write-Host "$name final: $($final.Value)"
    Write-Host "$name final bytes: $($bytesFinal.Value)"
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
        "--hidden", "--frames", "2", "--verify-sectors", "--screenshot", $screenshot)
    if ($exerciseReset) { $allArgs += "--exercise-reset" }
    [string[]]$lines = @(& $command @allArgs 2>&1 | ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
    $lines | ForEach-Object { Write-Host $_ }
    [IO.File]::WriteAllLines($screenshot + ".log", $lines)
    $text = [string]::Join("`n", $lines)
    if ($exitCode -ne 0) { throw "$name failed with exit code $exitCode." }
    Assert-WorldLog $name $text
    Assert-WorldFinal $name $text
    Assert-WorldBmp $name $screenshot
}

function Test-StreamingCapacity {
    Write-Host "=== Streaming capacity block ==="
    $exe = Join-Path $root "src/SharpGTA.D3D9/bin/Release/net40/SharpGTA.D3D9.exe"
    [string[]]$lines = @(& $exe --world --game iii --root $gameRootFull `
        --hidden --frames 2 --verify-sectors --stream-budget 20 --txd-budget 2 2>&1 |
        ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
    $lines | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0) { throw "Streaming capacity probe failed with code $exitCode." }
    $text = [string]::Join("`n", $lines)
    $stream = [regex]::Match($text,
        "World-Streaming: models=(\d+)/20 txds=(\d+)/2 evictedModels=\d+ evictedTxds=\d+ remaining=(\d+) deferred=(\d+)")
    if (-not $stream.Success -or [int]$stream.Groups[1].Value -le 0 -or
        [int]$stream.Groups[2].Value -ne 2 -or [int]$stream.Groups[3].Value -le 0 -or
        [int]$stream.Groups[4].Value -le 0) {
        throw "Streaming capacity did not remain explicitly queued."
    }
    if ($text -notmatch 'failedModels=0') {
        throw "Capacity-blocked TXD dependencies became permanent model failures."
    }
    Write-Host "PASS streaming capacity remains explicit and deferred: $($stream.Value)"
}

function Test-ByteBudget([string]$reference) {
    Write-Host "=== Async byte budget ==="
    $exe = Join-Path $root "src/SharpGTA.D3D9/bin/Release/net40/SharpGTA.D3D9.exe"
    $small = Join-Path $root "artifacts/world-smoke/byte-budget-small.bmp"
    [string[]]$lines = @(& $exe --world --game iii --root $gameRootFull `
        --hidden --frames 30 --exercise-reset --verify-sectors --byte-budget 1 `
        --screenshot $small 2>&1 | ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
    $lines | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0) { throw "Byte budget probe failed with code $exitCode." }
    $text = [string]::Join("`n", $lines)
    $bytes = [regex]::Match($text,
        "World-Bytes: cached=(\d+) bytes=(\d+)/(\d+) pending=(\d+) evicted=(\d+) failed=(\d+) oversize=(\d+)")
    if (-not $bytes.Success) {
        throw "Byte budget probe did not print async byte diagnostics."
    }
    $capacity = [long]$bytes.Groups[3].Value
    if ($capacity -ne 1048576L) {
        throw "Byte budget probe did not use a 1 MiB cache: $($bytes.Value)."
    }
    if ([int]$bytes.Groups[5].Value -le 0) {
        throw "Byte budget probe never evicted under pressure: $($bytes.Value)."
    }
    if ([int]$bytes.Groups[6].Value -ne 0 -or $text -notmatch 'failedModels=0') {
        throw "Byte budget pressure produced failures instead of streaming: $($bytes.Value)."
    }
    Assert-WorldLog "byte budget" $text
    Assert-WorldFinal "byte budget" $text
    Assert-WorldBmp "byte budget" $small
    # A byte budget smaller than the working set must not change the image, and a
    # payload larger than the whole budget must still be delivered to its decoder.
    if (-not (Test-Path $reference)) {
        throw "Byte budget comparison reference is missing: $reference."
    }
    $smallHash = (Get-FileHash $small).Hash
    $referenceHash = (Get-FileHash $reference).Hash
    if ($smallHash -ne $referenceHash) {
        throw "Byte budget changed the rendered world frame."
    }
    $oversize = [regex]::Match($text,
        "World-Bytes-Final: cached=\d+ bytes=\d+/(\d+) pending=\d+ evicted=\d+ failed=0 oversize=(\d+)")
    if (-not $oversize.Success) {
        throw "Byte budget probe did not report oversized payloads."
    }
    if ([int]$oversize.Groups[2].Value -le 0) {
        throw "Byte budget probe never exercised the oversized-delivery path."
    }
    Write-Host "PASS async byte budget is enforced without changing the frame: $($bytes.Value)"

    # Steady state: a large budget must settle instead of re-reading the same ring.
    [string[]]$steadyLines = @(& $exe --world --game iii --root $gameRootFull `
        --hidden --frames 30 --exercise-reset --verify-sectors 2>&1 |
        ForEach-Object { $_.ToString() })
    if ($LASTEXITCODE -ne 0) { throw "Byte steady-state probe failed." }
    $steadyText = [string]::Join("`n", $steadyLines)
    $steady = [regex]::Match($steadyText,
        "World-Bytes-Final: cached=(\d+) bytes=\d+/\d+ pending=(\d+) evicted=(\d+) failed=0 oversize=\d+")
    if (-not $steady.Success) {
        throw "Byte steady-state probe did not print final diagnostics."
    }
    if ([int]$steady.Groups[3].Value -ne 0 -or [int]$steady.Groups[4].Value -ne 0) {
        throw "Byte steady state kept re-reading or failing: $($steady.Value)."
    }
    if ([int]$steady.Groups[1].Value -le 0) {
        throw "Byte steady state cached no prefetched bytes: $($steady.Value)."
    }
    Write-Host "PASS async byte cache reaches steady state: $($steady.Value)"

    [string]$invalidError = Join-Path $root "artifacts/world-smoke/byte-budget-invalid.err.log"
    [string]$invalidOutput = Join-Path $root "artifacts/world-smoke/byte-budget-invalid.out.log"
    $invalidProcess = Start-Process -FilePath $exe -WorkingDirectory $root -PassThru -Wait `
        -ArgumentList @("--world", "--game", "iii", "--root", "`"$gameRootFull`"",
            "--hidden", "--frames", "2", "--byte-budget", "0") `
        -RedirectStandardError $invalidError -RedirectStandardOutput $invalidOutput
    if ($invalidProcess.ExitCode -ne 2) {
        throw "Invalid --byte-budget was not rejected: $((Get-Content $invalidError) -join ' ')"
    }
    if ((Get-Content $invalidError) -notmatch 'byte-budget') {
        throw "Invalid --byte-budget did not report the expected message."
    }
    Write-Host "PASS invalid --byte-budget is rejected"
}

function Test-WorldTime {
    Write-Host "=== Game clock and timed objects ==="
    $exe = Join-Path $root "src/SharpGTA.D3D9/bin/Release/net40/SharpGTA.D3D9.exe"
    $hours = @(12, 21, 3)
    $counts = @{}
    foreach ($hour in $hours) {
        $screenshot = Join-Path $root "artifacts/world-smoke/world-time-$hour.bmp"
        [string[]]$lines = @(& $exe --world --game iii --root $gameRootFull `
            --hidden --frames 3 --verify-sectors --time $hour --screenshot $screenshot 2>&1 |
            ForEach-Object { $_.ToString() })
        $exitCode = $LASTEXITCODE
        $lines | ForEach-Object { Write-Host $_ }
        if ($exitCode -ne 0) { throw "World time probe at $hour failed with code $exitCode." }
        $text = [string]::Join("`n", $lines)
        $time = [regex]::Match($text,
            "World-Time-Final: hour=(\d+):(\d+) day=(\d+) timed=(\d+)/(\d+) created=(\d+) destroyed=(\d+) rejected=(\d+)")
        if (-not $time.Success) {
            throw "World time probe at $hour did not print time diagnostics."
        }
        if ([int]$time.Groups[1].Value -ne $hour) {
            throw "World clock ignored --time $hour : $($time.Value)."
        }
        if ([int]$time.Groups[7].Value -ne 0) {
            throw "Timed object pool rejected placements at $hour : $($time.Value)."
        }
        if ([int]$time.Groups[8].Value -ne 0) {
            throw "Timed object pool overflowed at $hour : $($time.Value)."
        }
        $counts[$hour] = [int]$time.Groups[4].Value
        Assert-WorldLog "hour $hour" $text
        Assert-WorldFinal "hour $hour" $text
        Assert-WorldBmp "hour $hour" $screenshot
    }
    if ($counts[12] -le 0) {
        throw "No day timed objects were allocated at 12:00."
    }
    if ($counts[21] -le $counts[12]) {
        throw "Night timed objects were not allocated at 21:00 (day=$($counts[12]) night=$($counts[21]))."
    }
    Write-Host "PASS game clock drives timed-object pool: 12:00=$($counts[12]) 21:00=$($counts[21]) 03:00=$($counts[3])"

    [string]$invalidError = Join-Path $root "artifacts/world-smoke/time-invalid.err.log"
    [string]$invalidOutput = Join-Path $root "artifacts/world-smoke/time-invalid.out.log"
    $invalidProcess = Start-Process -FilePath $exe -WorkingDirectory $root -PassThru -Wait `
        -ArgumentList @("--world", "--game", "iii", "--root", "`"$gameRootFull`"",
            "--hidden", "--frames", "2", "--time", "24") `
        -RedirectStandardError $invalidError -RedirectStandardOutput $invalidOutput
    if ($invalidProcess.ExitCode -ne 2) {
        throw "Invalid --time was not rejected: $((Get-Content $invalidError) -join ' ')"
    }
    if ((Get-Content $invalidError) -notmatch '--time') {
        throw "Invalid --time did not report the expected message."
    }
    Write-Host "PASS invalid --time is rejected"
}

function Test-SpawnedCars([string]$reference) {
    Write-Host "=== Path-following cars ==="
    $exe = Join-Path $root "src/SharpGTA.D3D9/bin/Release/net40/SharpGTA.D3D9.exe"
    $output = Join-Path $root "artifacts/world-smoke"
    [IO.Directory]::CreateDirectory($output) | Out-Null
    $early = Join-Path $output "world-cars-early.bmp"
    $late = Join-Path $output "world-cars-late.bmp"
    $again = Join-Path $output "world-cars-again.bmp"
    [string[]]$lateLog = @(& $exe --world --game iii --root $gameRoot --hidden --frames 60 `
        --spawn-cars 4 --screenshot $late 2>&1 | ForEach-Object { $_.ToString() })
    if ($LASTEXITCODE -ne 0) { throw "Spawned cars run failed." }
    [string[]]$earlyLog = @(& $exe --world --game iii --root $gameRoot --hidden --frames 2 `
        --spawn-cars 4 --screenshot $early 2>&1 | ForEach-Object { $_.ToString() })
    if ($LASTEXITCODE -ne 0) { throw "Spawned cars early run failed." }
    [string[]]$againLog = @(& $exe --world --game iii --root $gameRoot --hidden --frames 60 `
        --spawn-cars 4 --screenshot $again 2>&1 | ForEach-Object { $_.ToString() })
    if ($LASTEXITCODE -ne 0) { throw "Spawned cars repeat run failed." }

    $lateText = [string]::Join("`n", $lateLog)
    if ($lateText -notmatch "World-Cars: spawned=4 model=\S+ nearest=\S+ alive=4 completed=\d+ replans=\d+") {
        throw "Spawned cars were not reported: $lateText"
    }
    if ($lateText -notmatch "World-Dynamic: spawned=4 alive=4 drawn=[1-9]\d*") {
        throw "Spawned cars drew nothing: $lateText"
    }
    if ((Get-FileHash $late -Algorithm SHA256).Hash -eq
        (Get-FileHash $reference -Algorithm SHA256).Hash) {
        throw "Spawned cars did not change the frame."
    }
    if ((Get-FileHash $late -Algorithm SHA256).Hash -eq
        (Get-FileHash $early -Algorithm SHA256).Hash) {
        throw "Spawned cars did not move between two and sixty frames."
    }
    if ((Get-FileHash $late -Algorithm SHA256).Hash -ne
        (Get-FileHash $again -Algorithm SHA256).Hash) {
        throw "Spawned cars are not deterministic."
    }
    Write-Host "PASS spawned cars follow the network and move: $(($lateLog | Where-Object { $_ -match 'World-Cars:' }) -join ' ')"
}

function Test-PathOverlay([string]$reference) {
    Write-Host "=== Path graph overlay ==="
    $exe = Join-Path $root "src/SharpGTA.D3D9/bin/Release/net40/SharpGTA.D3D9.exe"
    $output = Join-Path $root "artifacts/world-smoke"
    $first = Join-Path $output "world-overlay-a.bmp"
    $second = Join-Path $output "world-overlay-b.bmp"
    [string[]]$log = @()
    foreach ($screenshot in @($first, $second)) {
        $log = @(& $exe --world --game iii --root $gameRoot --hidden --frames 2 `
            --path-overlay --screenshot $screenshot 2>&1 | ForEach-Object { $_.ToString() })
        if ($LASTEXITCODE -ne 0) { throw "Path overlay run failed." }
    }
    $text = [string]::Join("`n", $log)
    $overlay = [regex]::Match($text, "Path-Overlay: segments=(\d+) nodes=(\d+)")
    if (-not $overlay.Success -or [int]$overlay.Groups[1].Value -le 0) {
        throw "Path overlay did not draw any segments: $text"
    }
    if ((Get-FileHash $first -Algorithm SHA256).Hash -ne
        (Get-FileHash $second -Algorithm SHA256).Hash) {
        throw "Path overlay is not deterministic."
    }
    if ((Get-FileHash $first -Algorithm SHA256).Hash -eq
        (Get-FileHash $reference -Algorithm SHA256).Hash) {
        throw "Path overlay did not change the frame."
    }

    # Both line kinds must really be on screen, not just a different frame.
    [byte[]]$bytes = [IO.File]::ReadAllBytes($first)
    $offset = [BitConverter]::ToInt32($bytes, 10)
    $width = [BitConverter]::ToInt32($bytes, 18)
    $height = [BitConverter]::ToInt32($bytes, 22)
    $car = 0
    $ped = 0
    for ($index = $offset; $index -lt $bytes.Length - 3; $index += 4) {
        $b = [int]$bytes[$index]
        $g = [int]$bytes[$index + 1]
        $r = [int]$bytes[$index + 2]
        if ($r -eq 255 -and $g -eq 160 -and $b -eq 63) { $car++ }
        elseif ($r -eq 63 -and $g -eq 208 -and $b -eq 255) { $ped++ }
    }
    if ($car -lt 100 -or $ped -lt 100) {
        throw "Path overlay colours are missing: car=$car ped=$ped."
    }
    Write-Host "PASS path overlay draws $($overlay.Groups[1].Value) segments (car=$car ped=$ped pixels)."

    # A resolved route must be drawn in its own colour on top of the network.
    $routeShot = Join-Path $output "world-route.bmp"
    [string[]]$routeLog = @(& $exe --world --game iii --root $gameRoot --hidden --frames 2 `
        --path-route 812 -936 36 700 -700 36 --screenshot $routeShot 2>&1 |
        ForEach-Object { $_.ToString() })
    if ($LASTEXITCODE -ne 0) { throw "Path route overlay run failed." }
    $routeText = [string]::Join("`n", $routeLog)
    if ($routeText -notmatch "Path-Overlay-Route: kind=Car .*hops=26 length=728\.49") {
        throw "Path route overlay did not resolve the expected route: $routeText"
    }
    [byte[]]$routeBytes = [IO.File]::ReadAllBytes($routeShot)
    $routeOffset = [BitConverter]::ToInt32($routeBytes, 10)
    $routePixels = 0
    for ($index = $routeOffset; $index -lt $routeBytes.Length - 3; $index += 4) {
        if ($routeBytes[$index] -eq 255 -and $routeBytes[$index + 1] -eq 255 -and
            $routeBytes[$index + 2] -eq 255) { $routePixels++ }
    }
    if ($routePixels -lt 100) {
        throw "Path route overlay drew no route pixels: $routePixels."
    }
    Write-Host "PASS path route overlay draws $routePixels pixels."
}

function Test-DynamicEntities([string]$reference) {
    Write-Host "=== Dynamic gameplay entities ==="
    $exe = Join-Path $root "src/SharpGTA.D3D9/bin/Release/net40/SharpGTA.D3D9.exe"
    $first = Join-Path $root "artifacts/world-smoke/world-spawn-a.bmp"
    $second = Join-Path $root "artifacts/world-smoke/world-spawn-b.bmp"
    $logs = @()
    foreach ($screenshot in @($first, $second)) {
        [string[]]$lines = @(& $exe --world --game iii --root $gameRootFull `
            --hidden --frames 3 --spawn 8 --verify-sectors --screenshot $screenshot 2>&1 |
            ForEach-Object { $_.ToString() })
        if ($LASTEXITCODE -ne 0) { throw "Spawn probe failed with code $LASTEXITCODE." }
        $logs += ,$lines
        Assert-WorldLog "spawn" ([string]::Join("`n", $lines))
        Assert-WorldFinal "spawn" ([string]::Join("`n", $lines))
    }
    $text = [string]::Join("`n", $logs[0])
    $dynamic = [regex]::Match($text,
        "World-Dynamic: spawned=(\d+) alive=(\d+) drawn=(\d+) rejected=(\d+) sectorMoves=(\d+) deferred=(\d+)")
    if (-not $dynamic.Success) {
        throw "Spawn probe did not print dynamic diagnostics."
    }
    if ([int]$dynamic.Groups[1].Value -ne 8 -or
        [int]$dynamic.Groups[2].Value -ne 8 -or
        [int]$dynamic.Groups[3].Value -le 0 -or
        [int]$dynamic.Groups[4].Value -ne 0 -or
        [int]$dynamic.Groups[6].Value -ne 0) {
        throw "Spawn probe has invalid dynamic state: $($dynamic.Value)."
    }
    if ($text -notmatch 'World-Entities: .* objects=8/450 ') {
        throw "Spawned objects are missing from the object pool."
    }
    if ((Get-FileHash $first).Hash -ne (Get-FileHash $second).Hash) {
        throw "Spawned entity frames are not deterministic."
    }
    if ((Get-FileHash $first).Hash -eq (Get-FileHash $reference).Hash) {
        throw "Spawned entities did not change the rendered frame."
    }
    Write-Host "PASS dynamic entities are simulated and rendered: $($dynamic.Value)"

    # A starved budget must still show the gameplay entities: they take the frame's first pick.
    [string[]]$tight = @(& $exe --world --game iii --root $gameRootFull `
        --hidden --frames 4 --stream-budget 20 --txd-budget 4 --spawn 8 2>&1 |
        ForEach-Object { $_.ToString() })
    if ($LASTEXITCODE -ne 0) { throw "Dynamic capacity-pressure probe failed." }
    $tightText = [string]::Join("`n", $tight)
    $tightDynamic = [regex]::Match($tightText,
        "World-Dynamic: spawned=8 alive=8 drawn=([1-9]\d*) rejected=0")
    if (-not $tightDynamic.Success) {
        throw "Gameplay entities were starved out by the static world: $tightText"
    }
    Write-Host "PASS gameplay entities survive a starved streaming budget: $($tightDynamic.Value)"

    [string[]]$plain = @(& $exe --world --game iii --root $gameRootFull `
        --hidden --frames 2 2>&1 | ForEach-Object { $_.ToString() })
    if (($plain -join "`n") -match 'World-Dynamic:') {
        throw "World-Dynamic is printed without --spawn."
    }
    Write-Host "PASS no dynamic diagnostics without --spawn"
}

function Test-MenuAndInput {
    Write-Host "=== New Game -> world and live input ==="
    $stdout = Join-Path $root "artifacts/world-smoke/menu-input.stdout.log"
    $stderr = Join-Path $root "artifacts/world-smoke/menu-input.stderr.log"
    $exe = Join-Path $root "src/SharpGTA.D3D9/bin/Release/net40/SharpGTA.D3D9.exe"
    $arguments = "--menu --game iii --root `"$gameRootFull`" --frames 100000 --verify-sectors"
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
    $heldRmb = $rmb[$rmb.Count - 1]
    $heldAfter = $wStates[1]
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
    Test-StreamingCapacity

    $reset = Join-Path $output "world-reset.bmp"
    Invoke-World "net40 (AnyCPU, Reset)" $net40 @() $reset -exerciseReset
    if ((Get-FileHash $baseline -Algorithm SHA256).Hash -ne (Get-FileHash $reset -Algorithm SHA256).Hash) {
        throw "Managed world resources changed the frame after Reset."
    }
    Write-Host "Reset preserves the world BMP exactly."
    Test-ByteBudget $reset
    Test-WorldTime
    Test-DynamicEntities $baseline
    Test-PathOverlay $baseline
    Test-SpawnedCars $baseline

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
