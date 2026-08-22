$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
dotnet restore .\SAM.slnx
dotnet build .\SAM.slnx -c Release --no-restore
dotnet test .\tests\SAM.Core.Tests\SAM.Core.Tests.csproj -c Release --no-build
dotnet publish .\src\SAM.Desktop\SAM.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\artifacts\SAM
Write-Host "`nSAM M0 build completed: $root\artifacts\SAM" -ForegroundColor Green
