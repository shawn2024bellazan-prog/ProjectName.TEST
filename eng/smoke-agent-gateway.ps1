$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
Set-Location -LiteralPath 'G:\ProjectName'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$receipt = "evidence\builds\gateway-$stamp.json"
$outLog = "evidence\builds\gateway-$stamp.stdout.txt"
$errLog = "evidence\builds\gateway-$stamp.stderr.txt"
$url = 'http://127.0.0.1:58440'
$dll = 'G:\ProjectName\src\Services\ProjectName.AgentGateway\bin\Release\net10.0\ProjectName.AgentGateway.dll'
$results = [ordered]@{ development='NOT_RUN'; production='NOT_RUN'; nativeProtocolsDenied=$false; throttled=$false; checkerPass=$false; productionEnabled=$false }
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(5)
function Assert-Status([System.Net.Http.HttpResponseMessage]$response, [int]$expected, [string]$name) {
  if ([int]$response.StatusCode -ne $expected) {
    throw "$name HTTP $([int]$response.StatusCode), expected $expected"
  }
}
function Start-Gateway([string]$environment) {
  $env:ASPNETCORE_ENVIRONMENT = $environment
  $env:DOTNET_ENVIRONMENT = $environment
  return Start-Process -FilePath 'dotnet' -ArgumentList @($dll, '--urls', $url) -PassThru -RedirectStandardOutput $outLog -RedirectStandardError $errLog
}
function Wait-Healthy {
  for ($i=0; $i -lt 40; $i++) {
    if ($proc -and $proc.HasExited) { throw "Gateway process exited before healthy response (code $($proc.ExitCode))" }
    try {
      $response = $client.GetAsync("$url/healthz").GetAwaiter().GetResult()
      if ($response.IsSuccessStatusCode) { return }
    } catch { }
    Start-Sleep -Milliseconds 250
  }
  throw 'Gateway health endpoint unavailable'
}
try {
  foreach ($environment in @('Development','Production')) {
    $proc = Start-Gateway $environment
    try {
      Wait-Healthy
      $manifest = $client.GetAsync("$url/v1/protocols").GetAwaiter().GetResult()
      Assert-Status $manifest 200 "$environment manifest"
      $body = $manifest.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
      if ($body.registeredAgents -ne 0 -or $body.executableEndpoints -ne 0 -or $body.activation -ne 'DENIED') {
        throw "$environment advertised unadmitted agent"
      }
      foreach ($path in @('/v1/responses','/v1/conversations','/a2a/role','/ag-ui','/.well-known/agent.json','/devui')) {
        $response = $client.GetAsync("$url$path").GetAwaiter().GetResult()
        Assert-Status $response 403 "$environment GET $path"
        $payload = [System.Net.Http.StringContent]::new('{}', [System.Text.Encoding]::UTF8, 'application/json')
        $post = $client.PostAsync("$url$path", $payload).GetAwaiter().GetResult()
        Assert-Status $post 403 "$environment POST $path"
      }
      $results.nativeProtocolsDenied = $true
      if ($environment -eq 'Development') {
        $wasThrottled = $false
        for ($n=0; $n -lt 40; $n++) {
          $response = $client.GetAsync("$url/healthz").GetAwaiter().GetResult()
          if ([int]$response.StatusCode -eq 429) { $wasThrottled = $true; break }
        }
        if (-not $wasThrottled) { throw 'Rate limiter failed to return 429' }
        $results.throttled = $true
      }
      $results[$environment.ToLowerInvariant()] = 'PASS'
    } finally {
      if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -ErrorAction SilentlyContinue }
      Start-Sleep -Milliseconds 750
    }
  }
  Write-Host 'AGENT_GATEWAY_SMOKE_PASS'
} catch {
  $results.failure = $_.Exception.Message
  Write-Host "AGENT_GATEWAY_SMOKE_HALT: $($results.failure)"
} finally {
  $client.Dispose()
  $results.timestampUtc = (Get-Date).ToUniversalTime().ToString('O')
  $results | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 -LiteralPath $receipt
  Write-Host "RECEIPT: $receipt"
}
if ($results.development -ne 'PASS' -or $results.production -ne 'PASS') { exit 2 }