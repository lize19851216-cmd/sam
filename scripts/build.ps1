$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$publishDirectories = @(".\artifacts\SAM", ".\artifacts\SAM.SteamBroker")
foreach ($directory in $publishDirectories) {
    if (Test-Path -LiteralPath $directory -PathType Container) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
}
$checksumPath = ".\artifacts\SHA256SUMS.txt"
if (Test-Path -LiteralPath $checksumPath -PathType Leaf) {
    Remove-Item -LiteralPath $checksumPath -Force
}
dotnet restore .\SAM.slnx -r win-x64
dotnet build .\SAM.slnx -c Release --no-restore
dotnet test .\tests\SAM.Core.Tests\SAM.Core.Tests.csproj -c Release --no-build
dotnet publish .\src\SAM.Desktop\SAM.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true --no-restore -o .\artifacts\SAM
dotnet publish .\src\SAM.SteamBroker\SAM.SteamBroker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true --no-restore -o .\artifacts\SAM.SteamBroker
$artifactRoot = Resolve-Path .\artifacts
Get-ChildItem $artifactRoot -Recurse -File | Where-Object Name -ne "SHA256SUMS.txt" |
    Get-FileHash -Algorithm SHA256 |
    Sort-Object Path |
    ForEach-Object { "$($_.Hash) *$($_.Path.Substring($artifactRoot.Path.Length + 1).Replace('\', '/'))" } |
    Set-Content (Join-Path $artifactRoot "SHA256SUMS.txt")
Write-Host "`nSAM build completed: $root\artifacts\SAM" -ForegroundColor Green
Write-Host "Steam authentication broker: $root\artifacts\SAM.SteamBroker" -ForegroundColor Green
Write-Host "SHA-256 checksums: $root\artifacts\SHA256SUMS.txt" -ForegroundColor Green
