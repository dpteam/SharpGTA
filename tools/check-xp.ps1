$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$shipping = Join-Path $root "src/SharpGTA.Headless/bin/Release/net40"
if (-not (Test-Path $shipping)) { throw "Missing net40 output: $shipping" }

function Read-U16([byte[]]$bytes, [int]$offset) { return [BitConverter]::ToUInt16($bytes, $offset) }
function Read-U32([byte[]]$bytes, [int]$offset) { return [BitConverter]::ToUInt32($bytes, $offset) }
function Read-U64([byte[]]$bytes, [int]$offset) { return [BitConverter]::ToUInt64($bytes, $offset) }

function Read-Ansi([byte[]]$bytes, [int]$offset) {
    if ($offset -lt 0 -or $offset -ge $bytes.Length) { throw "String offset is outside PE file." }
    $chars = New-Object System.Collections.Generic.List[char]
    for ($i = $offset; $i -lt $bytes.Length -and $i -lt $offset + 4096; $i++) {
        if ($bytes[$i] -eq 0) { return -join $chars }
        $chars.Add([char]$bytes[$i])
    }
    throw "Unterminated PE string."
}

function Test-PE([string]$path) {
    [byte[]]$bytes = [IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 256 -or (Read-U16 $bytes 0) -ne 0x5A4D) { throw "Not a PE file: $path" }
    [int]$pe = Read-U32 $bytes 0x3C
    if ((Read-U32 $bytes $pe) -ne 0x00004550) { throw "Missing PE signature: $path" }
    [int]$sections = Read-U16 $bytes ($pe + 6)
    [int]$optionalSize = Read-U16 $bytes ($pe + 20)
    [int]$optional = $pe + 24
    [int]$magic = Read-U16 $bytes $optional
    if ($magic -eq 0x10B) { $is64 = $false; $directory = $optional + 96 }
    elseif ($magic -eq 0x20B) { $is64 = $true; $directory = $optional + 112 }
    else { throw "Unknown PE optional header: $path" }

    $osMajor = Read-U16 $bytes ($optional + 40)
    $osMinor = Read-U16 $bytes ($optional + 42)
    $subMajor = Read-U16 $bytes ($optional + 48)
    $subMinor = Read-U16 $bytes ($optional + 50)
    if ($osMajor -gt 5 -or ($osMajor -eq 5 -and $osMinor -gt 1) -or
        $subMajor -gt 5 -or ($subMajor -eq 5 -and $subMinor -gt 1)) {
        throw "PE version exceeds XP 5.01 (${path}: OS $osMajor.$osMinor, subsystem $subMajor.$subMinor)."
    }

    $sectionTable = $optional + $optionalSize
    $sectionData = @()
    for ($i = 0; $i -lt $sections; $i++) {
        $offset = $sectionTable + $i * 40
        $sectionData += [pscustomobject]@{
            VirtualSize = Read-U32 $bytes ($offset + 8)
            VirtualAddress = Read-U32 $bytes ($offset + 12)
            RawSize = Read-U32 $bytes ($offset + 16)
            RawOffset = Read-U32 $bytes ($offset + 20)
        }
    }
    function Convert-Rva([uint32]$rva) {
        foreach ($section in $sectionData) {
            $span = [Math]::Max([uint32]$section.VirtualSize, [uint32]$section.RawSize)
            if ($rva -ge $section.VirtualAddress -and [uint64]$rva -lt [uint64]$section.VirtualAddress + $span) {
                $offset = [int64]$section.RawOffset + ([int64]$rva - $section.VirtualAddress)
                if ($offset -lt 0 -or $offset -ge $bytes.Length) { throw "RVA points outside file: $path" }
                return [int]$offset
            }
        }
        throw "Cannot map RVA 0x$($rva.ToString('X8')) in $path"
    }

    $imports = @()
    $importRva = Read-U32 $bytes ($directory + 8)
    if ($importRva -ne 0) {
        $descriptor = Convert-Rva $importRva
        for ($d = 0; $d -lt 4096; $d++) {
            $entry = $descriptor + $d * 20
            if ($entry + 20 -gt $bytes.Length) { throw "Import descriptor overflow: $path" }
            $lookup = Read-U32 $bytes $entry
            $nameRva = Read-U32 $bytes ($entry + 12)
            $firstThunk = Read-U32 $bytes ($entry + 16)
            if ($lookup -eq 0 -and $nameRva -eq 0 -and $firstThunk -eq 0) { break }
            $dll = Read-Ansi $bytes (Convert-Rva $nameRva)
            if ($lookup -eq 0) { $lookup = $firstThunk }
            $thunkOffset = Convert-Rva $lookup
            for ($t = 0; $t -lt 65536; $t++) {
                $entryOffset = $thunkOffset + $t * $(if ($is64) { 8 } else { 4 })
                $value = if ($is64) { Read-U64 $bytes $entryOffset } else { [uint64](Read-U32 $bytes $entryOffset) }
                if ($value -eq 0) { break }
                $ordinalFlag = if ($is64) { [Convert]::ToUInt64("8000000000000000", 16) } else { [uint64]2147483648 }
                if (($value -band $ordinalFlag) -ne 0) {
                    $imports += "$dll!#$($value -band 0xFFFF)"
                } else {
                    $nameOffset = Convert-Rva ([uint32]$value)
                    $imports += "$dll!$(Read-Ansi $bytes ($nameOffset + 2))"
                }
            }
            if ($t -eq 65536) { throw "Unterminated import thunk: $path" }
        }
    }

    foreach ($import in $imports) {
        if ($import -ine "mscoree.dll!_CorExeMain" -and $import -ine "mscoree.dll!_CorDllMain") {
            throw "Native import is not in the current XP allowlist: $import ($path). Update this check deliberately when adding Win32/D3D9."
        }
    }
    Write-Host "XP static check: $(Split-Path -Leaf $path), PE $osMajor.$osMinor/$subMajor.$subMinor, imports=$($imports.Count)"
}

# Managed DllImport declarations do not appear in the PE import table. Keep an
# explicit XP API allowlist so adding a newer API cannot silently pass this script.
$xpManagedApiAllowlist = @{}
$managedAssemblies = @()
$binaryFiles = Get-ChildItem $shipping -File | Where-Object { $_.Extension -in @(".exe", ".dll") } | Sort-Object Name
foreach ($file in $binaryFiles) {
    Test-PE $file.FullName
    $managedAssemblies += [Reflection.Assembly]::LoadFrom($file.FullName)
}

$managedPInvokeCount = 0
$methodFlags = [Reflection.BindingFlags]"Public,NonPublic,Static,Instance,DeclaredOnly"
foreach ($assembly in $managedAssemblies) {
    foreach ($type in $assembly.GetTypes()) {
        foreach ($method in $type.GetMethods($methodFlags)) {
            if (-not $method.IsPInvokeImpl) { continue }
            $managedPInvokeCount++
            $attributes = $method.GetCustomAttributes([Runtime.InteropServices.DllImportAttribute], $false)
            if ($attributes.Count -ne 1) { throw "Cannot inspect DllImport metadata: $($method.DeclaringType.FullName).$($method.Name)" }
            $attribute = $attributes[0]
            if ([string]::IsNullOrEmpty($attribute.EntryPoint)) {
                throw "P/Invoke must have an explicit EntryPoint for XP validation: $($method.DeclaringType.FullName).$($method.Name)"
            }
            $key = $attribute.Value + "!" + $attribute.EntryPoint
            if (-not $xpManagedApiAllowlist.ContainsKey($key)) {
                throw "Managed import is not in the XP allowlist: $key. Add the exact XP-compatible API deliberately."
            }
        }
    }
}
Write-Host "Managed P/Invoke declarations checked: $managedPInvokeCount"
Write-Host "XP static checks passed. A real XP SP3/.NET 4.0 boot test is still required."
