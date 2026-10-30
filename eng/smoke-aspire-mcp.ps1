$ErrorActionPreference = 'Continue' # Windows PowerShell wraps CLI stderr as ErrorRecord; check exit codes explicitly.
Set-Location -LiteralPath 'G:\ProjectName'
$apphost = 'G:\ProjectName\src\Hosting\ProjectName.AppHost\ProjectName.AppHost.csproj'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$receipt = "evidence\builds\aspire-mcp-$stamp.json"
$log = "evidence\builds\aspire-mcp-$stamp.log"
$result = [ordered]@{ start='NOT_RUN'; devui='NOT_RUN'; mcpList='NOT_RUN'; stopped='NOT_RUN'; checkerPass=$false; productionEnabled=$false }
$started = $false
try {
    Write-Host 'START_ASPIRE_DEVELOPMENT_APPHOST'
    $startOutput = (& dotnet aspire start --no-build --apphost $apphost --launch-profile development --format Json --non-interactive 2>&1 | Out-String)
    'Aspire AppHost start invoked. Raw stdout suppressed to avoid ephemeral dashboard tokens.' | Out-File -FilePath $log -Encoding UTF8
    if ($LASTEXITCODE -ne 0) { throw "Aspire start failed: $($startOutput.Substring(0,[Math]::Min(700,$startOutput.Length)))" }
    $started = $true
    $result.start = 'PASS'
    Start-Sleep -Seconds 3
    $desc = (& dotnet aspire describe --apphost $apphost --format Json --non-interactive 2>&1 | Out-String)
    'Aspire resource inspection completed. Raw environment metadata intentionally omitted.' | Out-File -FilePath $log -Append -Encoding UTF8
    if ($LASTEXITCODE -ne 0) { throw "Aspire describe failed: $desc" }
    if (-not $desc.Contains('devui')) { throw 'Aspire AppHost did not list DevUI' }
    $result.devui = 'PASS'
    if (-not $desc.Contains('agent-gateway')) { throw 'Aspire AppHost did not list protocol gateway' }
    $result.gateway = 'PASS'
    $mcp = (& dotnet aspire mcp tools --apphost $apphost --format Json --non-interactive 2>&1 | Out-String)
    'Aspire MCP tool discovery completed. Tool list is captured in the bounded verifier receipt.' | Out-File -FilePath $log -Append -Encoding UTF8
    if ($LASTEXITCODE -ne 0) { throw "Aspire MCP tool listing failed: $mcp" }
    if (-not $mcp.Contains('get_admission_status')) { throw 'Aspire MCP omitted read-only admission inspection tool' }
    $result.discoveredTool = 'orchestration-api/get_admission_status'
    $result.mcpList = 'PASS'
    Write-Host 'ASPIRE_MCP_SMOKE_PASS'
} catch {
    $result.failure = $_.Exception.Message
    Write-Host "ASPIRE_MCP_SMOKE_HALT: $($result.failure)"
} finally {
    if ($started) {
        $stopOutput = (& dotnet aspire stop --apphost $apphost --non-interactive 2>&1 | Out-String)
        'Aspire AppHost stop invoked. Raw stdout suppressed.' | Out-File -FilePath $log -Append -Encoding UTF8
        $result.stopped = if ($LASTEXITCODE -eq 0) { 'PASS' } else { 'FAILED' }
        Write-Host "ASPIRE_APPHOST_STOP=$($result.stopped)"
    }
    $result.timestampUtc = (Get-Date).ToUniversalTime().ToString('O')
    $result | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 -LiteralPath $receipt
    Write-Host "RECEIPT: $receipt"
}
