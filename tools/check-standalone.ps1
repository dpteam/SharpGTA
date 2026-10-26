$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $projects = @(
        "src/SharpGTA.Core/SharpGTA.Core.csproj",
        "src/SharpGTA.Games.III/SharpGTA.Games.III.csproj",
        "src/SharpGTA.Games.VC/SharpGTA.Games.VC.csproj",
        "src/SharpGTA.Headless/SharpGTA.Headless.csproj",
        "src/SharpGTA.Presentation/SharpGTA.Presentation.csproj",
        "src/SharpGTA.D3D9/SharpGTA.D3D9.csproj",
        "src/SharpGTA.Launcher/SharpGTA.Launcher.csproj"
    )
    foreach ($project in $projects) {
        [xml]$xml = Get-Content $project
        if ($xml.SelectNodes("//*[local-name()='PackageReference']").Count -ne 0) {
            throw "Shipping project contains PackageReference: $project"
        }
        $frameworks = $xml.SelectNodes("//*[local-name()='TargetFrameworks' or local-name()='TargetFramework']") |
            ForEach-Object { $_.InnerText }
        if (($frameworks -join ";") -notmatch "(^|;)net40($|;)") {
            throw "Shipping project does not target net40: $project"
        }

        $evaluationText = dotnet msbuild $project -nologo -p:TargetFramework=net40 -getItem:PackageReference | Out-String
        if ($LASTEXITCODE -ne 0) { throw "MSBuild evaluation failed: $project" }
        $evaluation = $evaluationText | ConvertFrom-Json
        foreach ($package in $evaluation.Items.PackageReference) {
            if ($package.Identity -ine "Microsoft.NETFramework.ReferenceAssemblies" -or
                $package.Version -ne "1.0.3" -or $package.PrivateAssets -ine "all") {
                throw "Unapproved evaluated package dependency in ${project}: $($package.Identity) $($package.Version)"
            }
        }
    }

    $shipping = "src/SharpGTA.Headless/bin/Release/net40"
    if (-not (Test-Path "$shipping/SharpGTA.Headless.exe")) {
        throw "Missing net40 headless build. Run dotnet build SharpGTA.sln -c Release."
    }
    $allowed = @(
        "SharpGTA.Headless.exe", "SharpGTA.Headless.exe.config", "SharpGTA.Headless.pdb",
        "SharpGTA.Core.dll", "SharpGTA.Core.pdb",
        "SharpGTA.Games.III.dll", "SharpGTA.Games.III.pdb",
        "SharpGTA.Games.VC.dll", "SharpGTA.Games.VC.pdb"
    )
    Get-ChildItem $shipping -File | ForEach-Object {
        if ($allowed -notcontains $_.Name) {
            throw "Unexpected file in standalone output: $($_.Name)"
        }
    }
    foreach ($required in @("SharpGTA.Core.dll", "SharpGTA.Games.III.dll", "SharpGTA.Games.VC.dll", "SharpGTA.Headless.exe.config")) {
        if (-not (Test-Path "$shipping/$required")) { throw "Missing standalone file: $required" }
    }
    $config = Get-Content "$shipping/SharpGTA.Headless.exe.config" -Raw
    if ($config -notmatch 'supportedRuntime\s+version="v4\.0"' -or
        $config -notmatch '\.NETFramework,Version=v4\.0') {
        throw "The executable config does not select .NET Framework 4.0."
    }

    $d3d9Shipping = "src/SharpGTA.D3D9/bin/Release/net40"
    if (-not (Test-Path "$d3d9Shipping/SharpGTA.D3D9.exe")) {
        throw "Missing net40 D3D9 build. Run dotnet build SharpGTA.sln -c Release."
    }
    $d3d9Allowed = @(
        "SharpGTA.D3D9.exe", "SharpGTA.D3D9.exe.config", "SharpGTA.D3D9.pdb",
        "SharpGTA.Core.dll", "SharpGTA.Core.pdb",
        "SharpGTA.Presentation.dll", "SharpGTA.Presentation.pdb",
        "SharpGTA.Games.III.dll", "SharpGTA.Games.III.pdb"
    )
    Get-ChildItem $d3d9Shipping -File | ForEach-Object {
        if ($d3d9Allowed -notcontains $_.Name) {
            throw "Unexpected file in D3D9 output: $($_.Name)"
        }
    }
    foreach ($required in @("SharpGTA.Core.dll", "SharpGTA.Presentation.dll", "SharpGTA.Games.III.dll", "SharpGTA.D3D9.exe.config")) {
        if (-not (Test-Path "$d3d9Shipping/$required")) { throw "Missing D3D9 file: $required" }
    }
    $d3d9Config = Get-Content "$d3d9Shipping/SharpGTA.D3D9.exe.config" -Raw
    if ($d3d9Config -notmatch 'supportedRuntime\s+version="v4\.0"' -or
        $d3d9Config -notmatch '\.NETFramework,Version=v4\.0') {
        throw "The D3D9 executable config does not select .NET Framework 4.0."
    }

    $launcherShipping = "src/SharpGTA.Launcher/bin/Release/net40"
    if (-not (Test-Path "$launcherShipping/SharpGTA.Launcher.exe")) {
        throw "Missing net40 Launcher build. Run dotnet build SharpGTA.sln -c Release."
    }
    $launcherAllowed = @(
        "SharpGTA.Launcher.exe", "SharpGTA.Launcher.exe.config", "SharpGTA.Launcher.pdb",
        "SharpGTA.Core.dll", "SharpGTA.Core.pdb",
        "SharpGTA.Presentation.dll", "SharpGTA.Presentation.pdb"
    )
    Get-ChildItem $launcherShipping -File | ForEach-Object {
        if ($launcherAllowed -notcontains $_.Name) {
            throw "Unexpected file in Launcher output: $($_.Name)"
        }
    }
    foreach ($required in @("SharpGTA.Core.dll", "SharpGTA.Presentation.dll", "SharpGTA.Launcher.exe.config")) {
        if (-not (Test-Path "$launcherShipping/$required")) { throw "Missing Launcher file: $required" }
    }
    $launcherConfig = Get-Content "$launcherShipping/SharpGTA.Launcher.exe.config" -Raw
    if ($launcherConfig -notmatch 'supportedRuntime\s+version="v4\.0"' -or
        $launcherConfig -notmatch '\.NETFramework,Version=v4\.0') {
        throw "The Launcher executable config does not select .NET Framework 4.0."
    }

    [xml]$props = Get-Content "Directory.Build.props"
    $brand = $props.SelectNodes("//*[local-name()='BrandName']")[0].InnerText
    foreach ($executable in @("$shipping/SharpGTA.Headless.exe", "$d3d9Shipping/SharpGTA.D3D9.exe", "$launcherShipping/SharpGTA.Launcher.exe")) {
        $fileInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path $executable).Path)
        if ($fileInfo.ProductName -cne $brand -or $fileInfo.CompanyName -cne $brand) {
            throw "Assembly branding is not centralized: Product=$($fileInfo.ProductName), Company=$($fileInfo.CompanyName), expected $brand."
        }
    }

    $ignoreRules = Get-Content ".gitignore" | ForEach-Object { $_.Trim() } |
        Where-Object { $_ -ne "" -and -not $_.StartsWith("#") }
    if ($ignoreRules -cnotcontains "refs/") {
        throw "refs/ is not present in .gitignore."
    }
    if (Get-Command git -ErrorAction SilentlyContinue) {
        $trackedRefs = git ls-files refs
        if ($LASTEXITCODE -ne 0) { throw "git ls-files failed." }
        if ($trackedRefs) { throw "Files under refs/ are tracked: $trackedRefs" }
        $ignoredProbe = git check-ignore --no-index -q -- "refs/.sharpgta-ignore-probe"
        if ($LASTEXITCODE -ne 0) { throw "git check-ignore does not ignore refs/." }
    }
    Write-Host "Standalone check passed: net40 Headless/D3D9/Launcher, project-only files, only compile-only targeting pack, centralized branding, refs/ ignored."
}
finally {
    Pop-Location
}
