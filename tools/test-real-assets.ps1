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
    $net9 = "src/SharpGTA.Headless/bin/Release/net9.0-windows/SharpGTA.Headless.dll"
    if (-not (Test-Path $net40) -or -not (Test-Path $net9)) {
        throw "Build Release first: dotnet build SharpGTA.sln -c Release"
    }

    function Invoke-Case([string]$name, [string]$executable, [string[]]$arguments, [bool]$modern) {
        Write-Host "=== $name ==="
        $output = if ($modern) { & dotnet $executable @arguments 2>&1 } else { & $executable @arguments 2>&1 }
        $code = $LASTEXITCODE
        $text = $output | Out-String
        if ($code -ne 0) { throw "$name failed with exit code ${code}:`n$text" }
        $summary = $output | Where-Object { "$_" -match "^(Assets:|Entities:|World-Data:|Path-Graph:|Path-Groups:|Path-Lanes:|Path-Lights:|Path-Follow:|Path-Traffic:|GXT:|RW:|DFF:|DFF-Scan:|TXD:|TXD-Scan:|Ground:|Zone:|Path:|Path-Nearest:|Path-Route:|Path-Verify:|Building:|Render-Preview:|Render-Resources:)" }
        $summary | ForEach-Object { Write-Host "$_" }
        return $text
    }

    foreach ($case in $available) {
        $game = $case.Game
        $gameRoot = $case.Root
        $common = @("--game", $game, "--root", $gameRoot)
        $ground = if ($game -eq "iii") { @("812", "-936") } else { @("-864", "-575") }
        $zoneZ = if ($game -eq "iii") { "36" } else { "10" }
        $standard = Invoke-Case "$game standard and world queries net40" $net40 ($common + @("--standard", "--verify-paths", "--query-ground", $ground[0], $ground[1], "-50", "100", "--query-zone", $ground[0], $ground[1], $zoneZ, "--query-path", "0", "0", "--query-path-nearest", $ground[0], $ground[1], $zoneZ, "--path-radius", "200", "--ticks", "1")) $false
        if ($standard -notmatch "dat=2" -or $standard -notmatch "models=[1-9]" -or $standard -notmatch "instances=[1-9]") {
            throw "$game standard output has no loaded models/instances."
        }
        if ($standard -notmatch "Ground: hit=true model=") { throw "$game real static ground query failed." }
        # Entity pools must cover every placement with original pool sizes and no overflow.
        if ($standard -notmatch "Entities: buildings=\d+/\d+ dummies=\d+/\d+ treadables=\d+/\d+ objects=\d+/\d+ rejected=0 objectData=[1-9]") {
            throw "$game entity pools did not cover every map placement."
        }
        $entitySplit = if ($game -eq "iii") {
            "buildings=5140/5500 dummies=2336/2802 treadables=1213/1214 objects=0/450 rejected=0"
        } else {
            "buildings=6778/7000 dummies=2336/2340 treadables=0/1 objects=0/460 rejected=0"
        }
        if ($standard -notmatch [regex]::Escape("Entities: $entitySplit")) {
            throw "$game entity classification changed: expected '$entitySplit'."
        }
        if ($standard -notmatch "Zone: navigation=" -or $standard -notmatch "Path: group=0 node=0 resolved=true") {
            throw "$game zone/path query failed."
        }
        if ($game -eq "iii" -and ($standard -notmatch "Asset-Issues: total=4" -or
            $standard -notmatch "navigation=42 information=42 mapZones=21 cull=744 occl=0 pathGroups=870 pathNodes=10440" -or
            $standard -notmatch "Zone: navigation=ROADBR1:Navig")) {
            throw "III MAPZONE/path data changed or was not loaded."
        }
        if ($game -eq "vc" -and ($standard -notmatch "navigation=12 information=165 mapZones=33 cull=631 occl=344 pathGroups=1957 pathNodes=23484 detached=1957 water=108 unresolved=0" -or
            $standard -notmatch "Zone: navigation=HAVANA:Default info=ICCREAM:Info")) {
            throw "VC zone/path data changed or was not loaded."
        }
        $graph = if ($game -eq "iii") {
            "Path-Graph: mergeDistance=1 tiles=2163 nodes=4947 car=1971 ped=2976 water=0 links=10272 crossTile=3200 crossings=512 carLinks=2079 deadEnds=158 laneFallbacks=4 unpairedExtern=325 unresolvedExtern=1 externalNext=1745 danglingNext=0 selfNext=0 externalSlots=6726 emptySlots=14691 unknownSlots=0 unresolvedGroups=34"
        } else {
            "Path-Graph: mergeDistance=1 tiles=1957 nodes=9642 car=2973 ped=6291 water=378 links=20350 crossTile=2456 crossings=36 carLinks=3064 deadEnds=199 laneFallbacks=4 unpairedExtern=178 unresolvedExtern=0 externalNext=1619 danglingNext=0 selfNext=0 externalSlots=5090 emptySlots=8752 unknownSlots=0 unresolvedGroups=0"
        }
        if ($standard -notmatch [regex]::Escape($graph)) {
            throw "$game path graph changed: expected '$graph'."
        }
        $verify = if ($game -eq "iii") {
            "Path-Verify: nodes=4947 links=10272 selfLinks=0 asymmetricLinks=0 overLengthLinks=0 distanceMismatch=0 overLengthCar=0 overLengthPed=0 overLengthWater=0 longest=186.61"
        } else {
            "Path-Verify: nodes=9642 links=20350 selfLinks=0 asymmetricLinks=0 overLengthLinks=26 distanceMismatch=0 overLengthCar=0 overLengthPed=0 overLengthWater=26 longest=329.02"
        }
        if ($standard -notmatch [regex]::Escape($verify)) {
            throw "$game path graph audit changed: expected '$verify'."
        }
        if ($standard -notmatch "Path-Verify: .*consistent=true") {
            throw "$game path graph is internally inconsistent."
        }
        $groups = if ($game -eq "iii") {
            "Path-Groups: components=109 car=3 ped=106 water=0 singles=20 largest=1967"
        } else {
            "Path-Groups: components=42 car=4 ped=32 water=6 singles=0 largest=5102"
        }
        if ($standard -notmatch [regex]::Escape($groups)) {
            throw "$game flood-fill groups changed: expected '$groups'."
        }
        # Lane counts come from the extern crossings; the offsets cars use depend on them, so a
        # silent change here would move every car sideways.
        $lanes = if ($game -eq "iii") {
            "Path-Lanes: laneWidth=5 maxLeft=2 maxRight=2 oneWay=573 multiLane=1209"
        } else {
            "Path-Lanes: laneWidth=5 maxLeft=3 maxRight=3 oneWay=629 multiLane=231"
        }
        if ($standard -notmatch [regex]::Escape($lanes)) {
            throw "$game lane data changed: expected '$lanes'."
        }
        # Junction lights are scanned from placed light models, so a broken scan shows up as a
        # zero here instead of silently letting cars drive through every red.
        $lights = if ($game -eq "iii") {
            "Path-Lights: lights=333 links=313 type1=161 type2=152 direction=172 pedLinks=402"
        } else {
            "Path-Lights: lights=271 links=271 type1=155 type2=116 direction=140 pedLinks=20"
        }
        if ($standard -notmatch [regex]::Escape($lights)) {
            throw "$game traffic light scan changed: expected '$lights'."
        }
        # Peds must wait on light-controlled crossings, not step onto the road on a red. III routes
        # cross flagged links within 1200 ticks; VC flags 20 crossings but the probe spawns (the
        # first ped nodes in graph order, capped by the 140 ped pool) never reach one, so that
        # half is only covered by pedLinks= above and by the synthetic follower test.
        $pedsWait = Invoke-Case "$game ped crossings" $net40 ($common + @("--standard",
            "--spawn-peds", "60", "--ticks", "1200")) $false
        if ($game -eq "iii" -and
            $pedsWait -notmatch "Path-Follow: vehicles=0 peds=60 actors=60 .*holds=[1-9]") {
            throw "III peds did not wait on a light-controlled crossing: $pedsWait"
        }
        if ($game -eq "vc" -and $pedsWait -notmatch "Path-Follow: vehicles=0 peds=60 actors=60 ") {
            throw "VC ped run did not keep its population: $pedsWait"
        }
        $nearestNode = if ($game -eq "iii") { "node=1969 group=87 local=1" } else { "node=139 group=31 local=3" }
        $nearest = "Path-Nearest: kind=car position=($($ground[0]),$($ground[1]),$zoneZ) radius=200 found=true $nearestNode"
        if ($standard -notmatch [regex]::Escape($nearest)) {
            throw "$game car path lookup did not find the expected node."
        }
        # Nothing within fifty units of an empty patch of the map must report found=false.
        $missPath = Invoke-Case "$game empty path lookup" $net40 ($common + @("--standard", "--query-path-nearest", "3000", "3000", "100", "--path-radius", "50", "--ticks", "0")) $false
        if ($missPath -notmatch "Path-Nearest: kind=car .*found=false") {
            throw "$game path lookup invented a node far away from any path."
        }
        $pedPath = Invoke-Case "$game ped path lookup" $net40 ($common + @("--standard", "--query-path-nearest", $ground[0], $ground[1], $zoneZ, "--path-kind", "ped", "--path-radius", "200", "--ticks", "0")) $false
        $pedNode = if ($game -eq "iii") { "node=2245 group=165 local=0" } else { "node=9176 group=1885 local=4" }
        if ($pedPath -notmatch "Path-Nearest: kind=ped .*found=true $pedNode ") {
            throw "$game ped path lookup did not find the expected node."
        }
        # Cars and peds must spawn on the real networks with a real mesh: the engine's "null"
        # model is a valid record, so a summary that says "spawned" can still be invisible.
        $actors = Invoke-Case "$game spawned cars and peds" $net40 ($common + @("--standard",
            "--spawn-cars", "2", "--spawn-peds", "3", "--ticks", "120")) $false
        $actorLine = [regex]::Match($actors, "Path-Follow: vehicles=2 peds=3 actors=5.*")
        if (-not $actorLine.Success) {
            throw "$game actors did not spawn on both networks."
        }
        if ($actorLine.Value -notmatch "vehicleModel=(?!null)\S+" -or $actorLine.Value -notmatch "pedModel=(?!null)\S+") {
            throw "$game actors spawned on the null model: $($actorLine.Value)"
        }
        # Finished actors must be recycled: despawned from the world and replaced, so the counted
        # population stays at its target while the map keeps seeing traffic.
        $recycle = Invoke-Case "$game recycled traffic" $net40 ($common + @("--standard",
            "--spawn-cars", "3", "--recycle", "--spawn-car-speed", "40", "--ticks", "3000")) $false
        $traffic = [regex]::Match($recycle, "Path-Traffic: cars=3/3 spawned=(\d+) despawned=(\d+) recycled=(\d+)")
        if (-not $traffic.Success) {
            throw "$game traffic recycling was not reported: $recycle"
        }
        $spawnedCount = [int]$traffic.Groups[1].Value
        if ($spawnedCount -lt 3 -or
            $spawnedCount -ne [int]$traffic.Groups[2].Value -or
            $spawnedCount -ne [int]$traffic.Groups[3].Value) {
            throw "$game recycling counters disagree: $($traffic.Value)"
        }
        if ($recycle -notmatch "Path-Follow: vehicles=3 peds=0 actors=3 ") {
            throw "$game recycled population drifted away from its target: $recycle"
        }
        # Routing must follow real connections only: III is fragmented at the probe, VC connects.
        $route = if ($game -eq "iii") {
            Invoke-Case "iii path route" $net40 ($common + @("--standard", "--query-path-route", "812", "-936", "36", "700", "-700", "36", "--path-radius", "200", "--ticks", "0")) $false
        } else {
            Invoke-Case "vc path route" $net40 ($common + @("--standard", "--query-path-route", "-864", "-575", "10", "-700", "-400", "10", "--path-kind", "ped", "--path-radius", "300", "--ticks", "0")) $false
        }
        if ($game -eq "iii" -and $route -notmatch "Path-Route: kind=car .*found=true hops=26 length=728\.49") {
            throw "III path route did not find the expected 26-hop drive."
        }
        if ($game -eq "vc" -and $route -notmatch "Path-Route: kind=ped .*found=true hops=282 length=4847\.98") {
            throw "VC path route did not find the expected 282-hop walk."
        }
        if ($game -eq "vc") {
            # VC water lanes live in the same file but their own kind, so they must be reachable
            # only through --path-kind water.
            $waterPath = Invoke-Case "vc water path lookup" $net40 ($common + @("--standard", "--query-path-nearest", "-744", "-530", "5", "--path-kind", "water", "--path-radius", "300", "--ticks", "0")) $false
            if ($waterPath -notmatch "Path-Nearest: kind=water .*found=true node=7027 group=1458 local=0") {
                throw "VC water path lookup did not find the expected node."
            }
        }
        if ($game -eq "vc" -and $standard -notmatch "collisions=4[0-9][0-9][0-9]") {
            throw "VC embedded collision was not loaded; only a small loose collision subset is present."
        }
        $building = Invoke-Case "$game building handle" $net40 ($common + @("--standard", "--query-building", "257", "--ticks", "0")) $false
        if ($building -notmatch "Building: handle=257 stale=false model=") {
            throw "$game building handle did not resolve to a placement."
        }
        $stale = Invoke-Case "$game stale building handle" $net40 ($common + @("--standard", "--query-building", "258", "--ticks", "0")) $false
        if ($stale -notmatch "Building: handle=258 stale=true") {
            throw "$game stale building handle was not reported as stale."
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
        $preview = Invoke-Case "$game NullRenderer preview" $net40 ($common + @("--img", "MODELS/GTA3.IMG", "--dff-entry", "PLAYER.DFF", "--render-preview", "--ticks", "0")) $false
        if ($preview -notmatch "Render-Preview: source=PLAYER\.DFF backend=null" -or
            $preview -notmatch "triangles=[1-9]" -or $preview -notmatch "invalid=0" -or
            $preview -notmatch "Render-Resources: dictionaries=1 textures=[1-9].*missing=0") {
            throw "$game NullRenderer preview or player texture resolution failed."
        }
        $scan = Invoke-Case "$game full IMG DFF scan" $net40 ($common + @("--dff-scan", "--ticks", "0")) $false
        if ($scan -notmatch "DFF-Scan: files=[1-9][0-9]* failed=0" -or $scan -notmatch "geometries=[1-9]") {
            throw "$game full DFF scan reported malformed nonempty entries."
        }
        if ($scan -notmatch "skins=[1-9]" -or $scan -notmatch "hanim=[1-9]" -or $scan -notmatch "matfx=[1-9]") {
            throw "$game DFF HAnim/Skin/MatFX counters were not populated."
        }
        if ($game -eq "vc") {
            $fireman = Invoke-Case "VC split Skin" $net40 ($common + @("--img", "MODELS/GTA3.IMG", "--dff-entry", "FIREMAN.DFF", "--ticks", "0")) $false
            if ($fireman -notmatch "FIREMAN\.DFF clumps=1 geometries=1.*skins=1 hanim=25") {
                throw "VC FIREMAN.DFF split-skin probe failed."
            }
            if ($scan -notmatch "oldSkins=161 splitSkins=114" -or $scan -notmatch "matfx=2960") {
                throw "VC DFF plugin counts changed or were not decoded."
            }
        } else {
            if ($scan -notmatch "skins=18 oldSkins=18 splitSkins=0" -or $scan -notmatch "hanim=360 matfx=10") {
                throw "III DFF plugin counts changed or were not decoded."
            }
        }
        $ifp = Invoke-Case "$game IFP animation package" $net40 ($common + @("--ifp", "ANIM/ped.ifp", "--ticks", "0")) $false
        if ($game -eq "iii") {
            if ($ifp -notmatch "IFP: source=ANIM/ped\.ifp blocks=1 animations=236 sequences=3746 frames=84851 translated=239 scaled=8 longest=5") {
                throw "III ped.ifp aggregate counts changed or were not read."
            }
        } else {
            if ($ifp -notmatch "IFP: source=ANIM/ped\.ifp blocks=1 animations=234 sequences=5031 frames=87546 translated=171 scaled=110 longest=12\.167") {
                throw "VC ped.ifp aggregate counts changed or were not read."
            }
        }
        $walkIfp = Invoke-Case "$game IFP named animation" $net40 ($common + @("--ifp", "ANIM/ped.ifp", "--ifp-animation", "walk_civi", "--ticks", "0")) $false
        if ($game -eq "iii") {
            if ($walkIfp -notmatch "IFP-Animation: block=ped name=walk_civi sequences=16 frames=37 duration=1\.2") {
                throw "III walk_civi hierarchy changed or was not decoded."
            }
        } else {
            if ($walkIfp -notmatch "IFP-Animation: block=ped name=WALK_civi sequences=22 frames=36 duration=1\.167") {
                throw "VC WALK_civi hierarchy changed or was not decoded."
            }
        }
        if ($game -eq "iii") {
            $ifpPose = Invoke-Case "III IFP pose on PLAYER.DFF" $net40 ($common + @("--img", "MODELS/GTA3.IMG", "--ifp", "ANIM/ped.ifp", "--ifp-model", "IMG:PLAYER.DFF", "--ifp-animation", "walk_civi", "--ifp-time", "0.4", "--ticks", "0")) $false
            if ($ifpPose -notmatch "IFP-Pose: model=IMG:PLAYER\.DFF animation=walk_civi t=0\.4 looped=no frames=17 skinned=no bound=16 unbound=0 changed=16 maxShift=0\.8") {
                throw "III IFP pose was not sampled on PLAYER.DFF."
            }
            # III peds are a rigid hierarchy, so skinning must report exactly that and invent nothing.
            $ifpSkin = Invoke-Case "III IFP skin on PLAYER.DFF" $net40 ($common + @("--img", "MODELS/GTA3.IMG", "--ifp", "ANIM/ped.ifp", "--ifp-model", "IMG:PLAYER.DFF", "--ifp-animation", "walk_civi", "--ifp-time", "0.4", "--skin", "--ticks", "0")) $false
            if ($ifpSkin -notmatch "IFP-Skin: model=IMG:PLAYER\.DFF animation=walk_civi t=0\.4 clumps=1 skinned=0 rigid=16 unsupported=0 bones=0 vertices=0 moved=0 maxDelta=0 bindDelta=0") {
                throw "III PLAYER.DFF must stay a rigid hierarchy, not a skinned mesh."
            }
            # walk_civi carries a moving root, so its loop gap is the locomotion distance.
            $ifpPlay = Invoke-Case "III IFP playback clock" $net40 ($common + @("--img", "MODELS/GTA3.IMG", "--ifp", "ANIM/ped.ifp", "--ifp-model", "IMG:PLAYER.DFF", "--ifp-animation", "walk_civi", "--ifp-play", "2.5", "--ticks", "0")) $false
            if ($ifpPlay -notmatch "IFP-Play: model=IMG:PLAYER\.DFF animation=walk_civi ticks=75 duration=1\.2 t=0\.[0-9]+ finished=no wraps=2" -or
                $ifpPlay -notmatch "loopGap=1\.5") {
                throw "III walk_civi playback did not advance, wrap and report its root mover gap."
            }
        } else {
            $ifpPose = Invoke-Case "VC IFP pose on skinned ped" $net40 ($common + @("--img", "MODELS/GTA3.IMG", "--ifp", "ANIM/ped.ifp", "--ifp-model", "IMG:FIREMAN.DFF", "--ifp-animation", "WALK_civi", "--ifp-time", "0.4", "--ticks", "0")) $false
            if ($ifpPose -notmatch "IFP-Pose: model=IMG:FIREMAN\.DFF animation=WALK_civi t=0\.4 looped=no frames=25 skinned=yes bound=22 unbound=0 changed=23 maxShift=1\.7") {
                throw "VC IFP pose was not sampled on the skinned ped."
            }
            # Split-skin deformation is exact: bindDelta must be zero on the real bind pose.
            $ifpSkin = Invoke-Case "VC IFP skin on skinned ped" $net40 ($common + @("--img", "MODELS/GTA3.IMG", "--ifp", "ANIM/ped.ifp", "--ifp-model", "IMG:FIREMAN.DFF", "--ifp-animation", "WALK_civi", "--ifp-time", "0.4", "--skin", "--ticks", "0")) $false
            if ($ifpSkin -notmatch "IFP-Skin: model=IMG:FIREMAN\.DFF animation=WALK_civi t=0\.4 clumps=1 skinned=1 rigid=0 unsupported=0 bones=24 vertices=855 moved=855 maxDelta=1\.0[0-9]* bindDelta=0") {
                throw "VC split-skin deformation is missing or the bind pose does not round-trip."
            }
            # Old-format skins are reported instead of being skinned with a guessed mapping.
            $legacySkin = Invoke-Case "VC IFP skin on old-format skin" $net40 ($common + @("--img", "MODELS/GTA3.IMG", "--ifp", "ANIM/ped.ifp", "--ifp-model", "IMG:male01.DFF", "--ifp-animation", "WALK_civi", "--ifp-time", "0.4", "--skin", "--ticks", "0")) $false
            if ($legacySkin -notmatch "IFP-Skin-Unsupported: atomic=0 old=yes split=no" -or
                $legacySkin -notmatch "IFP-Skin: model=IMG:male01\.DFF .*skinned=0 rigid=0 unsupported=1") {
                throw "VC old-format skin must be reported as unsupported."
            }
            # WALK_civi is authored to loop seamlessly, so its gap must be zero.
            $ifpPlay = Invoke-Case "VC IFP playback clock" $net40 ($common + @("--img", "MODELS/GTA3.IMG", "--ifp", "ANIM/ped.ifp", "--ifp-model", "IMG:FIREMAN.DFF", "--ifp-animation", "WALK_civi", "--ifp-play", "3", "--ticks", "0")) $false
            if ($ifpPlay -notmatch "IFP-Play: model=IMG:FIREMAN\.DFF animation=WALK_civi ticks=90 duration=1\.167 t=0\.[0-9]+ finished=no wraps=2" -or
                $ifpPlay -notmatch "maxStep=0\.1" -or $ifpPlay -notmatch "loopGap=0 ") {
                throw "VC WALK_civi playback did not advance, wrap seamlessly and stay smooth."
            }
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
