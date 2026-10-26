param(
    [string]$Exe = "src/SharpGTA.Launcher/bin/Release/net40/SharpGTA.Launcher.exe",
    [string]$GameRoot = "refs/Grand Theft Auto 3"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ($env:OS -ne "Windows_NT") {
    Write-Host "SKIP: MSHTML launcher smoke requires Windows."
    return
}

Push-Location $root
try {
    $exePath = (Resolve-Path $Exe).Path
    $binDirectory = Split-Path -Parent $exePath
    $dataDirectory = Join-Path $root "artifacts\launcher-smoke-profile"
    if (Test-Path $dataDirectory) { Remove-Item $dataDirectory -Recurse -Force }
    $env:SHARPGTA_LAUNCHER_TEST_DATA_DIR = $dataDirectory
    Add-Type -AssemblyName System.Windows.Forms
    $assembly = [Reflection.Assembly]::LoadFrom($exePath)

    function New-Launcher {
        $form = $assembly.CreateInstance("SharpGTA.Launcher.LauncherForm")
        $form.Show()
        return $form
    }

    function Pump([int]$milliseconds) {
        $deadline = (Get-Date).AddMilliseconds($milliseconds)
        while ((Get-Date) -lt $deadline) {
            [System.Windows.Forms.Application]::DoEvents()
            Start-Sleep -Milliseconds 20
        }
    }

    function Wait-Document([System.Windows.Forms.Form]$form) {
        for ($i = 0; $i -lt 200; $i++) {
            Pump 50
            $browser = $form.Controls[0]
            if ($browser.Document -ne $null) {
                $kind = $browser.Document.InvokeScript("eval", @("typeof window.sharpGtaReceive"))
                if ($kind -eq "function") { return $browser }
            }
        }
        throw "The MSHTML document did not finish loading."
    }

    function Invoke-Page($browser, [string]$script) {
        Pump 50
        return $browser.Document.InvokeScript("eval", @($script))
    }

    $form = New-Launcher
    try {
        $browser = Wait-Document $form
        $seen = Invoke-Page $browser "String(window.__sharpGtaHostSeen===true)"
        if ($seen -ne "True") { throw "The HTML page did not reach the C# host." }
        $title = Invoke-Page $browser "document.title"
        $body = $browser.Document.Body.OuterHtml
        if ($title -like "*__BRAND*" -or $body -like "*__BRAND_*") {
            throw "A branding placeholder survived page assembly."
        }
        Write-Host "PASS embedded page, MSHTML document and host bridge ($title)"

        $before = Invoke-Page $browser "document.documentElement.className"
        Invoke-Page $browser "document.getElementById('themeToggle').onclick()" | Out-Null
        Pump 400
        $after = Invoke-Page $browser "document.documentElement.className"
        if ($before -eq $after) { throw "Launcher theme did not change." }
        Write-Host "PASS dark/light theme round-trip"

        Invoke-Page $browser "document.getElementById('gameVc').onclick()" | Out-Null
        $warning = Invoke-Page $browser "document.getElementById('vcWarning').className"
        if ($warning -like "*hidden*") { throw "VC frontend limitation is not visible." }
        Invoke-Page $browser "document.getElementById('gameIii').onclick()" | Out-Null
        Write-Host "PASS III/VC selection with explicit VC limitation"

        $resolvedRoot = (Resolve-Path $GameRoot).Path
        $quotedRoot = $resolvedRoot.Replace('\', '\\')
        Invoke-Page $browser "document.getElementById('root').value='$quotedRoot';document.getElementById('root').onchange();document.getElementById('language').value='1';document.getElementById('language').onchange();document.getElementById('resolution').value='1';document.getElementById('resolution').onchange();" | Out-Null
        Pump 600
        $preview = Invoke-Page $browser "document.getElementById('preview').innerHTML"
        if ($preview -notlike "*--menu --game iii*" -or
            $preview -notlike "*--language 1*" -or
            $preview -notlike "*--resolution 800x600*" -or
            $preview -notlike "*Grand Theft Auto 3*") {
            throw "Launcher preview is incomplete: $preview"
        }
        Write-Host "PASS command preview with quoted original path, language and resolution"
        Write-Host "  $preview"

        $env:SHARPGTA_LAUNCHER_BIN_DIR = $binDirectory
        $stateType = $assembly.GetType("SharpGTA.Launcher.LauncherState", $true)
        $runnerType = $assembly.GetType("SharpGTA.Launcher.GameRunner", $true)
        $decode = $stateType.GetMethod("Decode", [Reflection.BindingFlags]"Static,NonPublic")
        $find = $runnerType.GetMethod("FindExecutable", [Reflection.BindingFlags]"Static,NonPublic")
        $build = $runnerType.GetMethod("BuildArguments", [Reflection.BindingFlags]"Static,NonPublic", $null,
            [Type[]]@($stateType, [bool]), $null)
        $state = $decode.Invoke($null, @("iii`n$resolvedRoot`nlight`n1`n96`n1.35`n77`n88`n0`n0`n0`n1`n1`n1`n0.0075`n1"))
        $arguments = $build.Invoke($null, @($state, $false))
        if ($arguments -notlike "*--language 1*" -or $arguments -notlike "*--resolution 800x600*" -or
            $arguments -notlike "*--no-vsync*" -or $arguments -notlike "*--no-frame-limit*") {
            throw "Launcher argument builder lost settings: $arguments"
        }
        $executable = $find.Invoke($null, @($null))
        if (-not $executable -or -not (Test-Path $executable)) {
            throw "SharpGTA.D3D9.exe could not be resolved from the launcher build directory."
        }
        Write-Host "PASS argument builder and executable discovery"

        $env:SHARPGTA_LAUNCHER_SMOKE = "1"
        $env:SHARPGTA_LAUNCHER_BIN_DIR = $binDirectory
        $settingsType = $assembly.GetType("SharpGTA.Launcher.LauncherSettings", $true)
        $settingsType.GetProperty("DataDirectory",
            [Reflection.BindingFlags]"Static,NonPublic").GetValue($null, $null)
        $screenshot = Join-Path $dataDirectory "launcher-smoke.bmp"
        $settings = Join-Path $dataDirectory "launcher.ini"
        if (Test-Path $screenshot) { Remove-Item $screenshot -Force }
        if (Test-Path $settings) { throw "Portable smoke settings already exist: $settings" }
        Invoke-Page $browser "document.getElementById('launch').onclick()" | Out-Null
        Pump 1500
        $status = Invoke-Page $browser "document.getElementById('status').innerHTML"
        if ($status -like "Launcher error:*") { throw $status }
        $processProperty = $form.GetType().GetProperty("HasExitedGame")
        $successful = $processProperty.GetValue($form, $null)
        if (-not $successful) {
            throw "The HTML launch button did not produce a successful D3D9 frontend process."
        }
        if (-not (Test-Path $screenshot)) { throw "Launcher smoke screenshot was not created." }
        if (-not (Test-Path $settings)) { throw "Launcher settings were not persisted." }
        Write-Host "PASS HTML launch button -> real D3D9 frontend -> BMP and saved settings"

        $second = New-Launcher
        try {
            $secondBrowser = Wait-Document $second
            $language = Invoke-Page $secondBrowser "document.getElementById('language').value"
            $resolution = Invoke-Page $secondBrowser "document.getElementById('resolution').value"
            $loadedRoot = Invoke-Page $secondBrowser "document.getElementById('root').value"
            if ($language -ne "1" -or $resolution -ne "1" -or $loadedRoot -ne $resolvedRoot) {
                throw "Saved launcher state did not round-trip: root=$loadedRoot language=$language resolution=$resolution"
            }
            Write-Host "PASS persisted state reload"
        }
        finally {
            $second.Close()
            $second.Dispose()
        }
        Write-Host "MSHTML launcher smoke passed."
    }
    finally {
        $form.Close()
        $form.Dispose()
        Remove-Item Env:SHARPGTA_LAUNCHER_SMOKE -ErrorAction SilentlyContinue
        Remove-Item Env:SHARPGTA_LAUNCHER_BIN_DIR -ErrorAction SilentlyContinue
        Remove-Item Env:SHARPGTA_LAUNCHER_TEST_DATA_DIR -ErrorAction SilentlyContinue
    }
}
finally {
    Pop-Location
}
