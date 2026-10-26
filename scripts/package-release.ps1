#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$OutputDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$pluginRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd([IO.Path]::DirectorySeparatorChar)

function Assert-NoReparsePoint([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if (((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Symbolic links and junctions are not permitted in release paths: $cursor"
            }
        }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if ($parent -eq $cursor) { break }
        $cursor = $parent
    }
}

if (-not [IO.Path]::IsPathFullyQualified($OutputDirectory)) { throw 'OutputDirectory must be an absolute path to a NEW directory.' }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (Test-Path -LiteralPath $outputRoot) { throw 'Release output already exists; choose a new directory. Existing releases are never overwritten.' }
if ($outputRoot.Equals($pluginRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $outputRoot.StartsWith($pluginRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    $outputRoot -match '(?i)(^|[\\/])backups?[^\\/]*([\\/]|$)') {
    throw 'Release output must be outside the source plugin and backup directories.'
}
Assert-NoReparsePoint $outputRoot

# Deliberately enumerate files instead of copying directories recursively.
$files = [Collections.Generic.List[string]]::new()
@(
    'bin/ZCodePet.exe',
    'assets/aemeath-mini/LICENSE', 'assets/aemeath-mini/NOTICE.md',
    'assets/aemeath-mini/pet.json', 'assets/aemeath-mini/spritesheet.png', 'assets/aemeath-mini/spritesheet.webp',
    'scripts/bridge.cjs', 'scripts/hook.cjs', 'scripts/mcp-server.cjs',
    '.zcode-plugin/plugin.json', 'hooks/pet-events.json', 'skills/pixel-pet/SKILL.md',
    'package.json', 'LICENSE', 'README.md'
) | ForEach-Object { $files.Add($_) }
foreach ($optional in @('PRIVACY.md', 'THIRD-PARTY-NOTICES.md')) {
    if (Test-Path -LiteralPath (Join-Path $pluginRoot $optional) -PathType Leaf) { $files.Add($optional) }
}
foreach ($relative in $files) {
    $source = Join-Path $pluginRoot $relative
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required release file missing: $relative" }
    Assert-NoReparsePoint $source
    if ($relative -notmatch '\.(exe|png|webp)$') {
        $content = [IO.File]::ReadAllText($source)
        if ($content -match '(?im)(?:(?<![a-z0-9])[a-z]:[\\/]|(?<!\\)\\\\[a-z0-9][^\\\s]*\\(?!\\)|/(?:Users|home)/)') {
            throw "Release text contains an absolute machine-specific path: $relative"
        }
    }
}
$package = Get-Content -LiteralPath (Join-Path $pluginRoot 'package.json') -Raw | ConvertFrom-Json
$manifest = Get-Content -LiteralPath (Join-Path $pluginRoot '.zcode-plugin/plugin.json') -Raw | ConvertFrom-Json
$version = [string]$package.version
if ($version -notmatch '^\d+\.\d+\.\d+$' -or $manifest.version -cne $version -or $manifest.name -cne $package.name) {
    throw 'Package and plugin manifest must have identical names and numeric x.y.z versions.'
}
$binary = Join-Path $pluginRoot 'bin/ZCodePet.exe'
$binaryVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($binary)
if ($binaryVersion.ProductVersion -cne $version -or $binaryVersion.FileVersion -cne "$version.0") {
    throw "Executable version does not match manifest/package $version; build the current sources first."
}
$sourceHashes = @{}
foreach ($relative in $files) { $sourceHashes[$relative] = (Get-FileHash -LiteralPath (Join-Path $pluginRoot $relative) -Algorithm SHA256).Hash }
$signature = Get-AuthenticodeSignature -LiteralPath $binary

New-Item -Path $outputRoot -ItemType Directory -ErrorAction Stop | Out-Null
$portableRoot = Join-Path $outputRoot 'portable/zcode-pet-plugin'
$marketRoot = Join-Path $outputRoot 'marketplace'
$marketPluginRoot = Join-Path $marketRoot 'zcode-pet-plugin'
foreach ($destinationRoot in @($portableRoot, $marketPluginRoot)) {
    foreach ($relative in $files) {
        $destination = Join-Path $destinationRoot $relative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        [IO.File]::Copy((Join-Path $pluginRoot $relative), $destination, $false)
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -cne $sourceHashes[$relative]) {
            throw "Source changed during packaging: $relative. Output is incomplete; use a new output directory after resolving it."
        }
    }
}
$copiedPackage = Get-Content -LiteralPath (Join-Path $portableRoot 'package.json') -Raw | ConvertFrom-Json
$copiedManifest = Get-Content -LiteralPath (Join-Path $portableRoot '.zcode-plugin/plugin.json') -Raw | ConvertFrom-Json
$copiedVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $portableRoot 'bin/ZCodePet.exe'))
if ($copiedPackage.version -cne $version -or $copiedManifest.version -cne $version -or
    $copiedPackage.name -cne $package.name -or $copiedManifest.name -cne $package.name -or
    $copiedVersion.ProductVersion -cne $version -or $copiedVersion.FileVersion -cne "$version.0") {
    throw 'Release metadata changed during packaging. Output is incomplete; resolve the source changes and choose a new directory.'
}
$marketplace = [ordered]@{
    name = 'local-zcode-pixel-pet'
    plugins = @([ordered]@{
        name = $manifest.name; source = './zcode-pet-plugin'; version = $version
        description = $manifest.description; displayName = 'Aemeath Mini Pixel Pet'; category = 'utilities'
    })
}
$marketplace | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $marketRoot 'marketplace.json') -Encoding utf8NoBOM

function Write-Zip([string]$Directory, [string]$Destination) {
    $archive = [IO.Compression.ZipFile]::Open($Destination, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in (Get-ChildItem -LiteralPath $Directory -Recurse -File -Force | Sort-Object FullName)) {
            $name = [IO.Path]::GetRelativePath($Directory, $file.FullName).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
}
$portableZip = "zcode-pixel-pet-$version-portable.zip"
$marketZip = "zcode-pixel-pet-$version-marketplace.zip"
Write-Zip (Join-Path $outputRoot 'portable') (Join-Path $outputRoot $portableZip)
Write-Zip $marketRoot (Join-Path $outputRoot $marketZip)
[ordered]@{
    version = $version
    createdUtc = [DateTime]::UtcNow.ToString('o')
    executableSha256 = $sourceHashes['bin/ZCodePet.exe']
    authenticodeStatus = [string]$signature.Status
    signatureMeaning = 'Observed Authenticode status only; this is not antivirus clearance or publisher reputation certification.'
    packageFiles = @($files)
    archives = @($portableZip, $marketZip)
    scope = 'Offline packaging only. No installer, registry, scheduled task, network, or antivirus settings changes.'
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputRoot 'release-info.json') -Encoding utf8NoBOM
$checksumLines = foreach ($file in (Get-ChildItem -LiteralPath $outputRoot -Recurse -File -Force | Sort-Object FullName)) {
    $relative = [IO.Path]::GetRelativePath($outputRoot, $file.FullName).Replace('\', '/')
    '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
}
$checksumLines | Set-Content -LiteralPath (Join-Path $outputRoot 'SHA256SUMS.txt') -Encoding utf8NoBOM
[pscustomobject]@{ version = $version; output = $outputRoot; filesPerPlugin = $files.Count; authenticodeStatus = [string]$signature.Status }
