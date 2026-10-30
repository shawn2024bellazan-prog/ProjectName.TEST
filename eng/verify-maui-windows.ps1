$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath 'G:\ProjectName'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$output = "evidence\builds\maui-windows-$stamp.json"
$result = [ordered]@{ sdk=''; targetFramework='net10.0-windows10.0.19041.0'; build='NOT_RUN'; checkerPass=$false }
try {
  $result.sdk = (& dotnet --version | Out-String).Trim()
  if (-not $result.sdk.StartsWith('10.')) { throw 'WRONG_SDK' }
  & dotnet build apps\ProjectName.Maui\ProjectName.Maui.csproj -f net10.0-windows10.0.19041.0 -c Debug --no-restore -v:q '-p:TargetFrameworks=net10.0-windows10.0.19041.0'
  if ($LASTEXITCODE -ne 0) { throw "BUILD_FAILED_$LASTEXITCODE" }
  $result.build = 'PASS'
  Write-Host 'MAUI_WINDOWS_PASS'
} catch {
  $result.error = $_.Exception.Message
  $result.build = 'HALT'
  Write-Host "MAUI_WINDOWS_HALT: $($result.error)"
} finally {
  $result.timestampUtc = (Get-Date).ToUniversalTime().ToString('O')
  $result | ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath $output
  Write-Host "MAUI_RECEIPT: $output"
}
