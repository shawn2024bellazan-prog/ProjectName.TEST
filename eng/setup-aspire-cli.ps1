$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath 'G:\ProjectName'
if (-not (Test-Path '.config\dotnet-tools.json')) {
  & dotnet new tool-manifest
  if ($LASTEXITCODE -ne 0) { throw 'Tool manifest creation failed' }
}
& dotnet tool install Aspire.Cli --version 13.6.4 --local
if ($LASTEXITCODE -ne 0) { throw 'Local Aspire CLI install failed' }
Write-Host 'ASPIRE_CLI_LOCAL_INSTALL_PASS'
& dotnet tool run aspire -- --version
if ($LASTEXITCODE -ne 0) { throw 'Local Aspire CLI verification failed' }
