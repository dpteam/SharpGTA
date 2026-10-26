$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $projects = @(
        "src/SharpGTA.Core/SharpGTA.Core.csproj",
        "src/SharpGTA.Games.III/SharpGTA.Games.III.csproj",
        "src/SharpGTA.Games.VC/SharpGTA.Games.VC.csproj",
        "src/SharpGTA.Headless/SharpGTA.Headless.csproj"
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
    [xml]$props = Get-Content "Directory.Build.props"
    $brand = $props.SelectNodes("//*[local-name()='BrandName']")[0].InnerText
    $fileInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path "$shipping/SharpGTA.Headless.exe").Path)
    if ($fileInfo.ProductName -cne $brand -or $fileInfo.CompanyName -cne $brand) {
        throw "Assembly branding is not centralized: Product=$($fileInfo.ProductName), Company=$($fileInfo.CompanyName), expected $brand."
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
    Write-Host "Standalone check passed: net40, project-only files, only compile-only targeting pack, centralized branding, refs/ ignored."
}
finally {
    Pop-Location
}
