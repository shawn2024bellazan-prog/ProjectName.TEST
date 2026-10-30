$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath 'G:\ProjectName'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$receiptPath = "evidence\builds\devui-agents-$stamp.json"
$report = [ordered]@{ timestampUtc = (Get-Date).ToUniversalTime().ToString('O'); verdict='HALT'; mode='SIMULATION_ONLY'; checkerPass=$false; productionEnabled=$false; modelCalls=0 }
try {
    # Project only non-secret URLs from Aspire diagnostics; never store full output.
    $raw = (& dotnet aspire describe --apphost .\src\Hosting\ProjectName.AppHost\ProjectName.AppHost.csproj --format Json --non-interactive | Out-String)
    if ($LASTEXITCODE -ne 0) { throw 'Aspire describe failed' }
    $resources = @((ConvertFrom-Json -InputObject $raw).resources)
    $devui = @($resources | Where-Object { $_.displayName -eq 'devui' }) | Select-Object -First 1
    $gateway = @($resources | Where-Object { $_.displayName -eq 'agent-gateway' }) | Select-Object -First 1
    $devuiBase = (@($devui.urls) | Select-Object -First 1 -ExpandProperty url).TrimEnd('/').Replace('/devui','')
    $gatewayBase = @($gateway.urls | Where-Object { $_.name -eq 'http' }) | Select-Object -First 1 -ExpandProperty url
    $entities = Invoke-RestMethod -Uri "$devuiBase/v1/entities" -TimeoutSec 10
    $required = @('pmcro-orchestrator','pmcro-planner','pmcro-maker','pmcro-checker','pmcro-reflector',
        'csuite-ceo','csuite-cto','csuite-coo','csuite-cfo','csuite-cpo',
        'csuite-cmo','csuite-clo','csuite-chro','csuite-cso','csuite-cio')
    $found = @($entities.entities | ForEach-Object { $_.name })
    $report.entities = $found
    $report.allAgents = ($found.Count -eq 15 -and @($required | Where-Object { $_ -notin $found }).Count -eq 0)
    $coreManifest = Get-Content -Raw -LiteralPath 'marketplaces\pmcro-core\marketplace.json' | ConvertFrom-Json
    $csuiteManifest = Get-Content -Raw -LiteralPath 'marketplaces\csuite-marketplace\marketplace.json' | ConvertFrom-Json
    $manifestNames = @($coreManifest.roles | ForEach-Object { 'pmcro-' + $_ }) +
        @($csuiteManifest.agents | ForEach-Object { 'csuite-' + $_.role.ToLowerInvariant() })
    $report.manifestParity = ($manifestNames.Count -eq 15 -and
        @($manifestNames | Where-Object { $_ -notin $found }).Count -eq 0 -and
        $coreManifest.state -eq 'DRAFT_UNREGISTERED' -and
        $csuiteManifest.state -eq 'DRAFT_UNREGISTERED' -and
        -not $coreManifest.executionAllowed -and -not $csuiteManifest.activation)
    $catalog = Invoke-RestMethod -Uri "$gatewayBase/v1/agents/catalog" -TimeoutSec 10
    $report.catalogDenied = ($catalog.mode -eq 'SIMULATION_ONLY' -and $catalog.executionAuthorized -eq $false -and $catalog.modelInferenceEnabled -eq $false)
    $report.crossSiteDenied = $false
    try {
        Invoke-WebRequest -Uri "$gatewayBase/v1/agents/catalog" -UseBasicParsing -Headers @{
            Origin='http://evil.invalid'; 'Sec-Fetch-Site'='cross-site'
        } -TimeoutSec 10 | Out-Null
    } catch {
        if ($null -ne $_.Exception.Response) {
            $report.crossSiteDenied = ([int]$_.Exception.Response.StatusCode -eq 403)
        }
    }
    $body = @{
        model='agent-gateway/pmcro-orchestrator'
        input='bounded simulation-only smoke probe'
        metadata=@{ entity_id='agent-gateway/pmcro-orchestrator' }
    } | ConvertTo-Json -Depth 5
    $response = Invoke-RestMethod -Uri "$devuiBase/v1/responses" -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 20
    $responseText = $response | ConvertTo-Json -Depth 12 -Compress
    $report.responsesSimulation = $responseText.Contains('SIMULATION_ONLY')
    $executiveBody = @{
        model='agent-gateway/csuite-ceo'
        input='bounded simulation-only executive probe'
        metadata=@{ entity_id='agent-gateway/csuite-ceo' }
    } | ConvertTo-Json -Depth 5
    $executive = Invoke-RestMethod -Uri "$devuiBase/v1/responses" -Method Post -ContentType 'application/json' -Body $executiveBody -TimeoutSec 20
    $report.executiveSimulation = (($executive | ConvertTo-Json -Depth 12 -Compress).Contains('SIMULATION_ONLY'))
    $report.verdict = if ($report.allAgents -and $report.manifestParity -and $report.catalogDenied -and $report.responsesSimulation -and $report.executiveSimulation -and $report.crossSiteDenied) { 'ENGINEERING_PASS' } else { 'HALT' }
} catch {
    $report.error = $_.Exception.Message
    if ($null -ne $_.Exception.Response) {
        $responseStream = $_.Exception.Response.GetResponseStream()
        if ($null -ne $responseStream) {
            $reader = New-Object System.IO.StreamReader($responseStream)
            $detail = $reader.ReadToEnd()
            $report.errorDetail = $detail.Substring(0, [Math]::Min(1500, $detail.Length))
            $reader.Dispose()
        }
    }
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $receiptPath -Encoding UTF8
Write-Host "DEVUI_AGENTS_VERDICT=$($report.verdict)"
Write-Host "ENTITIES=$(@($report.entities).Count) ALL_AGENT_ROLES=$($report.allAgents)"
Write-Host "MANIFEST_PARITY=$($report.manifestParity)"
Write-Host "CATALOG_SIMULATION_DENY=$($report.catalogDenied)"
Write-Host "RESPONSES_SIMULATION=$($report.responsesSimulation)"
Write-Host "CSUITE_SIMULATION=$($report.executiveSimulation)"
Write-Host "CROSS_SITE_DENIED=$($report.crossSiteDenied)"
if ($report.error) { Write-Host "ERROR=$($report.error)" }
Write-Host "RECEIPT=$receiptPath"
if ($report.verdict -ne 'ENGINEERING_PASS') { exit 2 }
