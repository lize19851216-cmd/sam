[CmdletBinding()]
param(
    [Parameter()]
    [string] $ArtifactDirectory = (Join-Path $PSScriptRoot "..\artifacts")
)

$ErrorActionPreference = "Stop"
$artifactRoot = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$manifestPath = Join-Path $artifactRoot "SHA256SUMS.txt"
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Checksum manifest was not found: $manifestPath"
}

$rootPrefix = $artifactRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$seenPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$verified = 0

foreach ($line in Get-Content -LiteralPath $manifestPath) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    if ($line -notmatch '^(?<hash>[A-Fa-f0-9]{64}) \*(?<path>.+)$') {
        throw "Invalid checksum manifest entry: $line"
    }

    $relativePath = $Matches.path.Replace('/', [IO.Path]::DirectorySeparatorChar)
    if ([IO.Path]::IsPathRooted($relativePath)) {
        throw "Checksum manifest contains an absolute path: $relativePath"
    }

    $filePath = [IO.Path]::GetFullPath((Join-Path $artifactRoot $relativePath))
    if (-not $filePath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Checksum manifest path escapes the artifact directory: $relativePath"
    }
    if (-not $seenPaths.Add($relativePath)) {
        throw "Checksum manifest contains a duplicate path: $relativePath"
    }
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
        throw "Published artifact is missing: $relativePath"
    }

    $actualHash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash
    if (-not [string]::Equals($actualHash, $Matches.hash, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Checksum mismatch: $relativePath"
    }
    $verified++
}

if ($verified -eq 0) { throw "Checksum manifest contains no artifact entries." }

foreach ($publishedFile in Get-ChildItem -LiteralPath $artifactRoot -Recurse -File | Where-Object FullName -ne $manifestPath) {
    $publishedRelativePath = [IO.Path]::GetRelativePath($artifactRoot, $publishedFile.FullName)
    if (-not $seenPaths.Contains($publishedRelativePath)) {
        throw "Published artifact is missing from the checksum manifest: $publishedRelativePath"
    }
}

Write-Host "Verified $verified published artifact checksums." -ForegroundColor Green
