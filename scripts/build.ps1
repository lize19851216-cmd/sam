$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
dotnet restore .\SAM.slnx
dotnet build .\SAM.slnx -c Release --no-restore
dotnet test .\tests\SAM.Core.Tests\SAM.Core.Tests.csproj -c Release --no-build
dotnet publish .\src\SAM.Desktop\SAM.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\artifacts\SAM
dotnet publish .\src\SAM.SteamBroker\SAM.SteamBroker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\artifacts\SAM.SteamBroker
Write-Host "`nSAM build completed: $root\artifacts\SAM" -ForegroundColor Green
Write-Host "Steam authentication broker: $root\artifacts\SAM.SteamBroker" -ForegroundColor Green
