$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath 'G:\ProjectName'
$version = (& dotnet --version | Out-String).Trim()
if (-not $version.StartsWith('10.')) { throw "Unsupported SDK: $version" }
Write-Host "REGENERATING_LOCKS_WITH_SDK=$version"
& dotnet restore ProjectName.slnx --force-evaluate -v:q
if ($LASTEXITCODE -ne 0) { throw "Restore failed: $LASTEXITCODE" }
Write-Host 'LOCK_REGEN_PASS'
