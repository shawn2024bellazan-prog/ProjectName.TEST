$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
Set-Location -LiteralPath 'G:\ProjectName'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$receipt = "evidence\builds\dev-surfaces-$stamp.json"
$outLog = "evidence\builds\dev-surfaces-$stamp.stdout.txt"
$errLog = "evidence\builds\dev-surfaces-$stamp.stderr.txt"
$url = 'http://127.0.0.1:58439'
$dll = 'G:\ProjectName\src\Services\ProjectName.OrchestrationApi\bin\Release\net10.0\ProjectName.OrchestrationApi.dll'
$results = [ordered]@{ development='NOT_RUN'; production='NOT_RUN'; mcp='NOT_RUN'; simulation='NOT_RUN'; cyclesDenied=$false; checkerPass=$false; productionEnabled=$false }
$client = [System.Net.Http.HttpClient]::new()
function Assert-Status([System.Net.Http.HttpResponseMessage]$response, [int]$expected, [string]$name) {
    if ([int]$response.StatusCode -ne $expected) {
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        throw "$name HTTP $([int]$response.StatusCode), expected $expected; $($body.Substring(0,[Math]::Min(200,$body.Length)))"
    }
}
function Start-Api([string]$environment) {
    $env:ASPNETCORE_ENVIRONMENT = $environment
    $env:DOTNET_ENVIRONMENT = $environment
    return Start-Process -FilePath 'dotnet' -ArgumentList @($dll, '--urls', $url) -PassThru -RedirectStandardOutput $outLog -RedirectStandardError $errLog
}
function Wait-Healthy {
    for ($i=0; $i -lt 40; $i++) {
        try {
            $response = $client.GetAsync("$url/healthz").GetAwaiter().GetResult()
            if ($response.IsSuccessStatusCode) { return }
        } catch { }
        Start-Sleep -Milliseconds 250
    }
    throw 'API health endpoint unavailable'
}
try {
    $proc = Start-Api 'Development'
    try {
        Wait-Healthy
        $openapi = $client.GetAsync("$url/openapi/v1.json").GetAwaiter().GetResult()
        Assert-Status $openapi 200 'OpenAPI'
        $openapiText = $openapi.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $openapiText.Contains('/v1/cycles')) { throw 'OpenAPI lacks cycle route' }
        $scalar = $client.GetAsync("$url/scalar/v1").GetAwaiter().GetResult()
        Assert-Status $scalar 200 'Scalar'
        $body = [System.Net.Http.StringContent]::new('{"trailId":"TS-DEV-ONLY","capabilityId":"candidate","actorId":"unregistered","payload":"none"}', [System.Text.Encoding]::UTF8, 'application/json')
        $response = $client.PostAsync("$url/v1/cycles", $body).GetAwaiter().GetResult()
        Assert-Status $response 403 'Admission'
        $results.cyclesDenied = $true
        $simulationInput = [System.Net.Http.StringContent]::new('{"trailId":"TS-DEV-SIM","rawIntent":"simulate only"}', [System.Text.Encoding]::UTF8, 'application/json')
        $simulationResponse = $client.PostAsync("$url/v1/workflows/simulate", $simulationInput).GetAwaiter().GetResult()
        Assert-Status $simulationResponse 200 'Native MAF simulation'
        $simulationOutput = $simulationResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        if ($simulationOutput.verdict -ne 'HALT' -or $simulationOutput.independentCheckerPass -ne $false -or $simulationOutput.productionExecutionAuthorized -ne $false) { throw 'Native MAF simulation failed closed' }
        $results.simulation = 'PASS'
        $req = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post,"$url/mcp")
        $req.Headers.Accept.ParseAdd('application/json')
        $req.Headers.Accept.ParseAdd('text/event-stream')
        $req.Headers.TryAddWithoutValidation('MCP-Protocol-Version','2026-07-28') | Out-Null
        $req.Headers.TryAddWithoutValidation('Mcp-Method','tools/list') | Out-Null
        $req.Content = [System.Net.Http.StringContent]::new('{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientCapabilities":{}}}}', [System.Text.Encoding]::UTF8, 'application/json')
        $mcpResponse = $client.SendAsync($req).GetAwaiter().GetResult()
        Assert-Status $mcpResponse 200 'MCP tools/list'
        $mcpBody = $mcpResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $mcpBody.ToLowerInvariant().Contains('admission')) { throw "MCP inspection tool missing: $($mcpBody.Substring(0,[Math]::Min(600,$mcpBody.Length)))" }
        $results.mcp = 'PASS'
        $results.development = 'PASS'
    } finally {
        if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -ErrorAction SilentlyContinue }
        Start-Sleep -Milliseconds 700
    }
    $proc = Start-Api 'Production'
    try {
        Wait-Healthy
        foreach ($path in @('/openapi/v1.json','/scalar/v1','/mcp','/v1/workflows/simulate')) {
            $response = $client.GetAsync("$url$path").GetAwaiter().GetResult()
            Assert-Status $response 404 "Production $path"
        }
        $productionInput = [System.Net.Http.StringContent]::new('{"trailId":"TS-DEV-SIM","rawIntent":"forbidden in production"}', [System.Text.Encoding]::UTF8, 'application/json')
        $productionSimulation = $client.PostAsync("$url/v1/workflows/simulate", $productionInput).GetAwaiter().GetResult()
        Assert-Status $productionSimulation 404 'Production simulation POST'
        $results.production = 'PASS'
    } finally {
        if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -ErrorAction SilentlyContinue }
    }
    Write-Host 'DEV_SURFACE_SMOKE_PASS'
} catch {
    $results.failure = $_.Exception.Message
    Write-Host "DEV_SURFACE_SMOKE_HALT: $($results.failure)"
} finally {
    $client.Dispose()
    $results.timestampUtc = (Get-Date).ToUniversalTime().ToString('O')
    $results | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 -LiteralPath $receipt
    Write-Host "RECEIPT: $receipt"
}
if ($results.development -ne 'PASS' -or $results.production -ne 'PASS') { exit 2 }
