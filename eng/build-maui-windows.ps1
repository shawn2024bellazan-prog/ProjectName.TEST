$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath 'G:\ProjectName'
$project = 'apps\ProjectName.Maui\ProjectName.Maui.csproj'
$tfm = 'net10.0-windows10.0.19041.0'
& dotnet restore $project "-p:TargetFrameworks=$tfm"
if ($LASTEXITCODE -ne 0) { throw "MAUI restore failed: $LASTEXITCODE" }
& dotnet build $project -f $tfm -c Debug --no-restore "-p:TargetFrameworks=$tfm"
if ($LASTEXITCODE -ne 0) { throw "MAUI build failed: $LASTEXITCODE" }
Write-Host 'MAUI_WINDOWS_BUILD_PASS'
