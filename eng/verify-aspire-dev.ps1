$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath 'G:\ProjectName'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$receiptPath = "evidence\builds\aspire-dev-$stamp.json"
$report = [ordered]@{ generatedUtc = (Get-Date).ToUniversalTime().ToString('O'); verdict = 'HALT'; productionEnabled = $false; checkerPass = $false; resources = @(); http = @() }
function ProbeHttp([string]$name, [string]$url, [int]$expected) {
    $status = 0
    try {
        $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 8
        $status = [int]$response.StatusCode
    } catch {
        if ($null -ne $_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
        }
    }
    return [ordered]@{ name = $name; expected = $expected; observed = $status; passed = ($status -eq $expected) }
}
try {
    # The CLI emits connection strings, dashboard credentials and OTLP secrets.
    # Never persist or print its raw JSON; project only non-sensitive fields.
    $raw = (& dotnet aspire describe --apphost .\src\Hosting\ProjectName.AppHost\ProjectName.AppHost.csproj --format Json --non-interactive | Out-String)
    if ($LASTEXITCODE -ne 0) { throw 'Aspire describe failed' }
    $resources = @((ConvertFrom-Json -InputObject $raw).resources)
    $names = @('agent-gateway','checker','devui','maker','model-orchestrator','ollama','orchestration-api','orchestrator','planner','postgres','projectname','redis','reflector','web')
    foreach ($name in $names) {
        $entry = @($resources | Where-Object { $_.displayName -eq $name }) | Select-Object -First 1
        if ($null -eq $entry) { $report.resources += [ordered]@{ name=$name; state='Missing'; health='Missing' } }
        else { $report.resources += [ordered]@{ name=$name; state=$entry.state; health=$entry.healthStatus } }
    }
    $api = @($resources | Where-Object { $_.displayName -eq 'orchestration-api' }) | Select-Object -First 1
    $gateway = @($resources | Where-Object { $_.displayName -eq 'agent-gateway' }) | Select-Object -First 1
    $devui = @($resources | Where-Object { $_.displayName -eq 'devui' }) | Select-Object -First 1
    $ollama = @($resources | Where-Object { $_.displayName -eq 'ollama' }) | Select-Object -First 1
    $apiUrl = @($api.urls | Where-Object { $_.name -eq 'http' }) | Select-Object -First 1 -ExpandProperty url
    $gatewayUrl = @($gateway.urls | Where-Object { $_.name -eq 'http' }) | Select-Object -First 1 -ExpandProperty url
    $devuiUrl = @($devui.urls) | Select-Object -First 1 -ExpandProperty url
    $ollamaUrl = @($ollama.urls | Where-Object { $_.name -eq 'http' }) | Select-Object -First 1 -ExpandProperty url
    $report.http += ProbeHttp 'api-health' "$apiUrl/healthz" 200
    $report.http += ProbeHttp 'scalar' "$apiUrl/scalar/v1" 200
    $report.http += ProbeHttp 'devui' $devuiUrl 200
    $report.http += ProbeHttp 'gateway-health' "$gatewayUrl/healthz" 200
    $report.http += ProbeHttp 'gateway-unauthorized-deny' "$gatewayUrl/v1/unauthorized" 403
    $report.http += ProbeHttp 'ollama-tags' "$ollamaUrl/api/tags" 200
    $modelPresent = $false
    $modelDigest = $null
    try {
        $tags = Invoke-RestMethod -Uri "$ollamaUrl/api/tags" -TimeoutSec 8
        $selected = @($tags.models | Where-Object { $_.name -eq 'qwen3:4b' }) | Select-Object -First 1
        $modelPresent = ($null -ne $selected)
        if ($modelPresent) { $modelDigest = $selected.digest }
    } catch { $modelPresent = $false }
    $localOnly = ($ollama.environment.OLLAMA_NO_CLOUD -eq '1')
    $report.model = [ordered]@{ name = 'qwen3:4b'; digest = $modelDigest; installed = $modelPresent; localOnly = $localOnly; inferenceInvoked = $false }
    $allHealthy = (@($report.resources | Where-Object { $_.state -ne 'Running' -or $_.health -ne 'Healthy' }).Count -eq 0)
    $allHttp = (@($report.http | Where-Object { -not $_.passed }).Count -eq 0)
    if ($allHealthy -and $allHttp -and $modelPresent -and $localOnly) { $report.verdict = 'ENGINEERING_PASS' }
} catch { $report.error = $_.Exception.Message }
$report | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $receiptPath -Encoding UTF8
Write-Host "ASPIRE_DEV_VERDICT=$($report.verdict)"
foreach ($res in $report.resources) { Write-Host "RESOURCE $($res.name): $($res.state) / $($res.health)" }
foreach ($probe in $report.http) { Write-Host "HTTP $($probe.name): $($probe.observed) expected $($probe.expected)" }
Write-Host "MODEL_INSTALLED=$($report.model.installed)"
Write-Host "RECEIPT=$receiptPath"
if ($report.verdict -ne 'ENGINEERING_PASS') { exit 2 }
