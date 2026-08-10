# Builds a distributable zip of Coffee Talk Access.
#
# The layout inside the zip mirrors where the files must END UP, so a user can
# drag each folder's contents across without deciding anything:
#
#   Mods\      -> <game>\Mods\        (managed: the mod + the speech wrapper)
#   GameRoot\  -> <game>\             (native x86 speech DLLs, next to the exe)
#
# The split is load-bearing, not cosmetic. UniversalSpeech is P/Invoked by bare
# name and then dlopens nvdaControllerClient.dll from ITS OWN directory, so the
# native pair must be co-located next to the exe. In Mods\ speech degrades to
# SAPI silently -- it still talks, which is exactly what makes it hard to spot.
#
# Usage:  .\package.ps1              -> dist\CoffeeTalkAccess-v<version>.zip
#         .\package.ps1 -SkipBuild   -> package whatever is already in bin\Release

param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# Version comes from the MelonInfo attribute, so the zip name can never drift
# from what the mod reports about itself in the log.
$mainCs = Join-Path $root 'src\Main.cs'
$melonInfo = Select-String -Path $mainCs -Pattern 'MelonInfo\(typeof\([^)]+\),\s*"[^"]*",\s*"([^"]+)"'
if (-not $melonInfo) { throw "Could not read the version from the MelonInfo attribute in $mainCs" }
$version = $melonInfo.Matches[0].Groups[1].Value
Write-Host "Version $version (from MelonInfo)"

if (-not $SkipBuild) {
    # Release, and deliberately NOT deployed to the game: packaging should not
    # depend on a game install being present or writable.
    Write-Host "Building Release..."
    & dotnet build (Join-Path $root 'CoffeeTalkAccess.csproj') -c Release -p:SkipDeploy=true
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }
}

$staging = Join-Path $root 'dist\staging'
$distDir = Join-Path $root 'dist'
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $staging 'Mods')     -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $staging 'GameRoot') -Force | Out-Null

# Copy explicitly by name and fail loudly on a miss. A wildcard copy would
# happily produce a zip that is missing the speech DLL, and the resulting
# package is silent in a way the user cannot diagnose.
function Copy-Required {
    param([string]$Source, [string]$Destination, [string]$What)
    if (-not (Test-Path $Source)) { throw "$What not found at $Source" }
    Copy-Item $Source -Destination $Destination -Force
}

$release = Join-Path $root 'bin\Release'
Copy-Required (Join-Path $release 'CoffeeTalkAccess.dll')     (Join-Path $staging 'Mods') 'The mod DLL'
Copy-Required (Join-Path $release 'UnityAccessibilityLib.dll') (Join-Path $staging 'Mods') 'UnityAccessibilityLib.dll'
Copy-Required (Join-Path $root 'libs\UniversalSpeech.dll')      (Join-Path $staging 'GameRoot') 'Native UniversalSpeech.dll'
Copy-Required (Join-Path $root 'libs\nvdaControllerClient.dll') (Join-Path $staging 'GameRoot') 'Native nvdaControllerClient.dll'
Copy-Required (Join-Path $root 'package\README.txt')            $staging 'README.txt'

# The native DLLs must be 32-bit or they throw BadImageFormatException at the
# first P/Invoke -- i.e. at the first spoken word, after everything else looks
# fine. Check the PE machine type here rather than shipping and finding out.
function Assert-X86 {
    param([string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    $machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
    # 0x014C = IMAGE_FILE_MACHINE_I386, 0x8664 = AMD64
    if ($machine -ne 0x014C) {
        throw ("{0} is not 32-bit (PE machine 0x{1:X4}). Coffee Talk is PE32/i386; a 64-bit build throws BadImageFormatException at P/Invoke time." -f (Split-Path $Path -Leaf), $machine)
    }
}
Assert-X86 (Join-Path $staging 'GameRoot\UniversalSpeech.dll')
Assert-X86 (Join-Path $staging 'GameRoot\nvdaControllerClient.dll')
Write-Host "Verified both native speech DLLs are 32-bit (x86)."

$zipPath = Join-Path $distDir "CoffeeTalkAccess-v$version.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zipPath
Remove-Item $staging -Recurse -Force

$sizeKb = [math]::Round((Get-Item $zipPath).Length / 1KB, 1)
Write-Host ""
Write-Host "Package: $zipPath  ($sizeKb KB)"
