$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $cases = @(
        @{ Game = "iii"; Root = "refs/Grand Theft Auto 3" },
        @{ Game = "vc"; Root = "refs/Grand Theft Auto Vice City" }
    )
    $available = @($cases | Where-Object { Test-Path $_.Root })
    if ($available.Count -eq 0) {
        Write-Host "SKIP real-asset tests: refs/Grand Theft Auto 3 and refs/Grand Theft Auto Vice City are absent."
        exit 0
    }

    $net40 = "src/SharpGTA.Headless/bin/Release/net40/SharpGTA.Headless.exe"
    $net9 = "src/SharpGTA.Headless/bin/Release/net9.0/SharpGTA.Headless.dll"
    if (-not (Test-Path $net40) -or -not (Test-Path $net9)) {
        throw "Build Release first: dotnet build SharpGTA.sln -c Release"
    }

    function Invoke-Case([string]$name, [string]$executable, [string[]]$arguments, [bool]$modern) {
        Write-Host "=== $name ==="
        $output = if ($modern) { & dotnet $executable @arguments 2>&1 } else { & $executable @arguments 2>&1 }
        $code = $LASTEXITCODE
        $text = $output | Out-String
        if ($code -ne 0) { throw "$name failed with exit code ${code}:`n$text" }
        $summary = $output | Where-Object { "$_" -match "^(Assets:|GXT:|RW:|DFF:|DFF-Scan:|TXD:|TXD-Scan:|Ground:)" }
        $summary | ForEach-Object { Write-Host "$_" }
        return $text
    }

    foreach ($case in $available) {
        $game = $case.Game
        $gameRoot = $case.Root
        $common = @("--game", $game, "--root", $gameRoot)
        $ground = if ($game -eq "iii") { @("812", "-936") } else { @("-864", "-575") }
        $standard = Invoke-Case "$game standard and ground net40" $net40 ($common + @("--standard", "--query-ground", $ground[0], $ground[1], "-50", "100", "--ticks", "1")) $false
        if ($standard -notmatch "dat=2" -or $standard -notmatch "models=[1-9]" -or $standard -notmatch "instances=[1-9]") {
            throw "$game standard output has no loaded models/instances."
        }
        if ($standard -notmatch "Ground: hit=true model=") { throw "$game real static ground query failed." }
        if ($game -eq "vc" -and $standard -notmatch "collisions=4[0-9][0-9][0-9]") {
            throw "VC embedded collision was not loaded; only a small loose collision subset is present."
        }
        $null = Invoke-Case "$game standard net9" $net9 ($common + @("--standard", "--ticks", "1")) $true
        $gxt = Invoke-Case "$game GXT" $net40 ($common + @("--gxt", "TEXT/AMERICAN.GXT", "--ticks", "0")) $false
        if ($gxt -notmatch "main=[1-9]") { throw "$game GXT main table is empty." }
        if ($game -eq "vc" -and $gxt -notmatch "missions=[1-9]") { throw "VC mission tables were not read." }
        $rw = Invoke-Case "$game IMG DFF inventory" $net40 ($common + @("--img", "MODELS/GTA3.IMG", "--rw-entry", "PLAYER.DFF", "--ticks", "0")) $false
        if ($rw -notmatch "RW: source=IMG:PLAYER\.DFF roots=1 chunks=[1-9]") {
            throw "$game PLAYER.DFF inventory failed."
        }
        $decoded = Invoke-Case "$game static DFF decode" $net40 ($common + @("--img", "MODELS/GTA3.IMG", "--dff-entry", "PLAYER.DFF", "--ticks", "0")) $false
        if ($decoded -notmatch "DFF: source=IMG:PLAYER\.DFF clumps=1 geometries=[1-9]") {
            throw "$game PLAYER.DFF static decode failed."
        }
        $scan = Invoke-Case "$game full IMG DFF scan" $net40 ($common + @("--dff-scan", "--ticks", "0")) $false
        if ($scan -notmatch "DFF-Scan: files=[1-9][0-9]* failed=0" -or $scan -notmatch "geometries=[1-9]") {
            throw "$game full DFF scan reported malformed nonempty entries."
        }
        $looseTxd = Invoke-Case "$game loose TXD" $net40 ($common + @("--txd", "MODELS/particle.txd", "--ticks", "0")) $false
        if ($looseTxd -notmatch "TXD: source=MODELS/particle\.txd declared=[1-9]" -or $looseTxd -notmatch "pixels=[1-9]") {
            throw "$game loose particle.txd decode failed."
        }
        if ($game -eq "iii") {
            $textureProbe = Invoke-Case "III uncompressed TXD" $net40 ($common + @("--img", "MODELS/GTA3.IMG", "--txd-entry", "PLAYER.TXD", "--ticks", "0")) $false
            if ($textureProbe -notmatch "textures=1 skipped=0 levels=1 pixels=65536 raw=1") {
                throw "III PLAYER.TXD decode failed."
            }
        } else {
            $textureProbe = Invoke-Case "VC DXT1 DXT3 raw TXD" $net40 ($common + @("--img", "MODELS/GTA3.IMG", "--txd-entry", "WMYBE.TXD", "--txd-entry", "AIRPLAN.TXD", "--txd-entry", "DOCENTRANCE.TXD", "--ticks", "0")) $false
            if ($textureProbe -notmatch "WMYBE\.TXD declared=1 textures=1 skipped=0 levels=9" -or
                $textureProbe -notmatch "AIRPLAN\.TXD declared=4 textures=4 skipped=0 levels=34" -or
                $textureProbe -notmatch "DOCENTRANCE\.TXD declared=2 textures=2 skipped=0 levels=2 pixels=8192 raw=2") {
                throw "VC texture format probes failed."
            }
        }
        $txdScan = Invoke-Case "$game full IMG TXD decode" $net40 ($common + @("--txd-scan", "--ticks", "0")) $false
        if ($txdScan -notmatch "TXD-Scan: files=[1-9][0-9]* failed=0" -or
            $txdScan -notmatch "textures=[1-9]" -or $txdScan -notmatch "decodedLevels=[1-9]" -or
            $txdScan -notmatch "pixels=[1-9]") {
            throw "$game full TXD decode failed."
        }
        if ($game -eq "iii") {
            $legacy = Invoke-Case "III legacy and multi-clump DFF" $net40 ($common + @("--dff", "MODELS/Generic/loplyguy.dff", "--dff", "MODELS/Generic/peds.dff", "--ticks", "0")) $false
            if ($legacy -notmatch "loplyguy\.dff clumps=1 geometries=1" -or $legacy -notmatch "peds\.dff clumps=22 geometries=352") {
                throw "III legacy/multi-clump DFF fixtures failed."
            }
        } else {
            $wheels = Invoke-Case "VC loose DFF" $net40 ($common + @("--dff", "MODELS/generic/wheels.DFF", "--ticks", "0")) $false
            if ($wheels -notmatch "wheels\.DFF clumps=1 geometries=20") { throw "VC wheels.DFF decode failed." }
        }
    }
    Write-Host "Real-asset smoke tests passed for $($available.Count) game tree(s). See docs/REAL-ASSETS.md for scope."
}
finally {
    Pop-Location
}
