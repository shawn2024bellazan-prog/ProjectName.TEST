$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath 'G:\ProjectName'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$log = "evidence\builds\core-$stamp.log"
$receipt = "evidence\builds\core-$stamp.json"
$result = [ordered]@{ sdk=''; restore='NOT_RUN'; build='NOT_RUN'; tests='NOT_RUN'; checkerPass=$false; productionEnabled=$false }
try {
  $result.sdk = (& dotnet --version | Out-String).Trim()
  Write-Host "SDK=$($result.sdk)"
  if (-not $result.sdk.StartsWith('10.')) { throw 'Wrong SDK for production baseline' }
  & dotnet restore ProjectName.slnx --locked-mode -v:q 2>&1 | Tee-Object -FilePath $log
  if ($LASTEXITCODE -ne 0) { throw 'RESTORE_FAILED' }
  $result.restore = 'PASS'
  & dotnet build ProjectName.slnx -c Release --no-restore -v:q 2>&1 | Tee-Object -FilePath $log -Append
  if ($LASTEXITCODE -ne 0) { throw 'BUILD_FAILED' }
  $result.build = 'PASS'
  & dotnet test tests\ProjectName.Governance.Tests\ProjectName.Governance.Tests.csproj -c Release --no-build -v:q 2>&1 | Tee-Object -FilePath $log -Append
  if ($LASTEXITCODE -ne 0) { throw 'TESTS_FAILED' }
  $result.tests = 'PASS'
  Write-Host 'CORE_VALIDATION_PASS'
} catch {
  $result.failure = $_.Exception.Message
  Write-Host "CORE_VALIDATION_HALT: $($result.failure)"
} finally {
  $result.timestampUtc = (Get-Date).ToUniversalTime().ToString('O')
  $result | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 -LiteralPath $receipt
  Write-Host "RECEIPT: $receipt"
}
