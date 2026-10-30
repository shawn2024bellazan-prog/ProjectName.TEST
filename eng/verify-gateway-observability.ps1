# Opt-in isolated developer verification. Default: synthetic fixtures only.
# Never starts Aspire, workers, models, or production agent routes.
param(
    [switch]$RunLiveGateway,
    [ValidateRange(1,3)][int]$LiveGatewayIterations = 1,
    [Parameter(Mandatory=$true)][string]$CoreClrReceiptName,
    [Parameter(Mandatory=$true)][string]$CoreClrReceiptSha256,
    [ValidateSet('None','Missing','Malformed','Failed')][string]$ReplayFault = 'None'
)
$ErrorActionPreference = 'Stop'
$root = 'G:\ProjectName'
Set-Location -LiteralPath $root
$watcher = Join-Path $root 'eng\watch-agent-gateway.ps1'
$correlator = Join-Path $root 'eng\correlate-gateway-exits.ps1'
$validator = Join-Path $root 'tests\fixtures\validate-gateway-correlation.ps1'
$guard = Join-Path $root 'tests\fixtures\test-gateway-port-guard.ps1'
$contract = Join-Path $root 'eng\gateway-receipt-contract.ps1'
$contractTests = Join-Path $root 'tests\fixtures\test-gateway-receipt-contract.ps1'
$concurrentGuard = Join-Path $root 'tests\fixtures\test-gateway-concurrent-port-guard.ps1'
$pinManifest = Join-Path $root 'eng\coreclr-receipts.development.json'
$pinTests = Join-Path $root 'tests\fixtures\test-coreclr-receipt-pin.ps1'
$eventInspector = Join-Path $root 'eng\inspect-coreclr-events.ps1'
$replayScript = Join-Path $root 'tests\fixtures\test-coreclr-rotation-replay.ps1'
$replayMock = Join-Path $root 'eng\coreclr-rotation-mock-replay.ps1'
$replayGate = Join-Path $root 'eng\coreclr-replay-gate.ps1'
$replayGateTests = Join-Path $root 'tests\fixtures\test-coreclr-replay-gate.ps1'
$replayNegativeTests = Join-Path $root 'tests\fixtures\test-gateway-replay-denials.ps1'
$appHostSource = Join-Path $root 'src\Hosting\ProjectName.AppHost\AppHost.cs'
$gatewaySource = Join-Path $root 'src\Services\ProjectName.AgentGateway\Program.cs'
$apiSource = Join-Path $root 'src\Services\ProjectName.OrchestrationApi\Program.cs'
. $contract
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fffffff'
$nonce = ([guid]::NewGuid()).ToString('N').Substring(0,8)
$receipt = "evidence\builds\gateway-observability-$stamp-$nonce.json"
$watchNames = [Collections.Generic.List[string]]::new()
$concurrentNames = [Collections.Generic.List[string]]::new()
$validation = [Collections.Generic.List[string]]::new()
function Get-Sha256([string]$path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [Convert]::ToHexString($sha.ComputeHash([IO.File]::ReadAllBytes($path))) }
    finally { $sha.Dispose() }
}
function Invoke-BoundedCommand([string]$script, [string[]]$arguments, [int]$expectedExit, [string]$receiptLabel) {
    $lines = @(& pwsh -NoProfile -NonInteractive -File $script @arguments 2>&1 | ForEach-Object { $_.ToString() })
    $code = $LASTEXITCODE
    if ($code -ne $expectedExit) { throw "Expected $expectedExit from $receiptLabel, got $code." }
    $match = @($lines | Where-Object { $_ -like "$receiptLabel=*" })
    if ($match.Count -ne 1) { throw "Missing unique $receiptLabel receipt." }
    $value = $match[0].Substring($receiptLabel.Length + 1)
    if ($value -cnotmatch '^evidence\\builds\\gateway-watchdog-\d{8}-\d{6}-\d{7}-[0-9a-f]{8}\.json$' -and
        $value -cnotmatch '^evidence\\builds\\gateway-correlation-\d{8}-\d{6}-\d{7}-[0-9a-f]{8}\.json$') {
        throw 'Unexpected receipt path.'
    }
    return ($value.Split('\') | Select-Object -Last 1)
}
function Invoke-WatchFixture([string]$mode, [int]$iterations, [int]$timeout, [int]$expectedExit) {
    $name = Invoke-BoundedCommand $watcher @('-Mode',$mode,'-Iterations',"$iterations",'-TimeoutSeconds',"$timeout") $expectedExit 'GATEWAY_WATCHDOG_RECEIPT'
    $data = Get-Content -LiteralPath (Join-Path $root "evidence\builds\$name") -Raw | ConvertFrom-Json
    if ($data.mode -ne $mode -or $data.checkerPass -ne $false -or $data.productionActivationAllowed -ne $false -or
        $data.scope -ne 'ISOLATED_DEVELOPMENT_ONLY' -or @($data.runs).Count -ne $iterations) {
        throw "Watchdog fixture invariants rejected for $mode."
    }
    if ($expectedExit -eq 0 -and $data.result -ne 'PASS') { throw "Expected clean watchdog $mode." }
    if ($expectedExit -eq 2 -and $data.result -ne 'LOOP') { throw "Expected bounded LOOP for $mode." }
    $watchNames.Add($name)
    $validation.Add("$mode=$($data.result)")
}
function Assert-PortAvailable {
    $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,58440)
    $probe.Server.ExclusiveAddressUse = $true
    try { $probe.Start() }
    finally { $probe.Stop() }
}
function Assert-NegativeCall([string]$case, [string[]]$arguments) {
    $null = @(& pwsh -NoProfile -NonInteractive -File $correlator @arguments 2>&1)
    if ($LASTEXITCODE -eq 0) { throw "Negative input $case unexpectedly accepted." }
    $validation.Add("negative:$case=REJECTED")
}
$report = [ordered]@{
    schema = 'projectname.gateway-observability/v1'
    scope = 'ISOLATED_DEVELOPMENT_ONLY'
    syntheticByDefault = $true
    liveGatewayExplicitlyOptedIn = [bool]$RunLiveGateway
    liveGatewayIterations = if ($RunLiveGateway) { $LiveGatewayIterations } else { 0 }
    maxGatewayIterations = 3
    status = 'NOT_STARTED'
    sourceHashes = [ordered]@{}
    watchdogReceiptNames = @()
    watchdogReceiptSha256 = @()
    concurrentRefusalReceiptNames = @()
    concurrentRefusalReceiptSha256 = @()
    contractAssertionCount = 0
    pinnedInputReceiptName = $null
    pinnedInputReceiptSha256 = $null
    pinAndFieldAssertions = 0
    replayGateAssertions = 0
    replayAssertions = 0
    replaySourceIsolation = 'NOT_CHECKED'
    replayFault = $ReplayFault
    correlationReceiptName = $null
    correlationReceiptSha256 = $null
    assertions = @()
    rootCauseProven = $false
    checkerPass = $false
    productionActivationAllowed = $false
    fullAspireStarted = $false
    workerStarts = 0
    modelCalls = 0
}
try {
    foreach ($file in @($watcher,$correlator,$validator,$guard,$contract,$contractTests,$concurrentGuard,$pinManifest,$pinTests,$eventInspector,$replayScript,$replayMock,$replayGate,$replayGateTests,$replayNegativeTests,$appHostSource,$gatewaySource,$apiSource)) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw 'Verification component missing.' }
        $hashKey = if ($file -ceq $gatewaySource) { 'AgentGateway/Program.cs' } elseif ($file -ceq $apiSource) { 'OrchestrationApi/Program.cs' } else { [IO.Path]::GetFileName($file) }
        $report.sourceHashes[$hashKey] = Get-Sha256 $file
    }
    if ($CoreClrReceiptName -cnotmatch '^coreclr-events-\d{8}-\d{6}-\d{7}-[0-9a-f]{8}\.json$') { throw 'Redacted receipt name rejected.' }
    $observationFile = Join-Path $root "evidence\builds\$CoreClrReceiptName"
    $observationItem = Get-Item -LiteralPath $observationFile -ErrorAction Stop
    if ($observationItem.Length -lt 1 -or $observationItem.Length -gt 1048576) { throw 'Redacted receipt size invalid.' }
    $observationActualHash = Get-Sha256 $observationFile
    $allowlist = Get-Content -LiteralPath $pinManifest -Raw | ConvertFrom-Json
    $observation = Get-Content -LiteralPath $observationFile -Raw | ConvertFrom-Json
    Assert-PinnedCoreClrObservation $allowlist $observation $CoreClrReceiptName $CoreClrReceiptSha256 $observationActualHash ([datetimeoffset]::UtcNow)
    $report.pinnedInputReceiptName = $CoreClrReceiptName
    $report.pinnedInputReceiptSha256 = $observationActualHash
    $pinLines = @(& pwsh -NoProfile -NonInteractive -File $pinTests 2>&1 | ForEach-Object { $_.ToString() })
    if ($LASTEXITCODE -ne 0 -or $pinLines -notcontains 'PINNED_CORECLR_CONTRACTS=PASS' -or
        $pinLines -notcontains 'PIN_AND_FIELD_ASSERTIONS=17') { throw 'Pinned receipt and XML field-name tests failed.' }
    $report.pinAndFieldAssertions = 17
    $validation.Add('pinnedObservationAndNamedField=PASS_17')
    # Pre-child mock replay gate: failures HALT before concurrent watchdog or any fixture child.
    . $replayGate
    $runtimeSources = @(
        [IO.File]::ReadAllText($appHostSource),
        [IO.File]::ReadAllText($gatewaySource),
        [IO.File]::ReadAllText($apiSource)
    )
    Assert-MockReplaySourceIsolation $runtimeSources
    $report.replaySourceIsolation = 'PASS'
    $gateLines = @(& pwsh -NoProfile -NonInteractive -File $replayGateTests 2>&1 | ForEach-Object { $_.ToString() })
    if ($LASTEXITCODE -ne 0 -or $gateLines -notcontains 'REPLAY_GATE_CONTRACTS=PASS' -or
        $gateLines -notcontains 'REPLAY_GATE_ASSERTIONS=11') { throw 'Mock replay gate contract test failed.' }
    $report.replayGateAssertions = 11
    $replayLines = @(& pwsh -NoProfile -NonInteractive -File $replayScript 2>&1 | ForEach-Object { $_.ToString() })
    $replayExit = $LASTEXITCODE
    # Negative harness injection can only force a denial; it cannot bypass this gate.
    if ($ReplayFault -ceq 'Missing') { $replayLines = @() }
    elseif ($ReplayFault -ceq 'Malformed') { $replayLines = @('MOCK_ROTATION_REPLAY=PASS','MOCK_REPLAY_ASSERTIONS=invalid','CRYPTOGRAPHIC_AUTHORITY_TESTED=false','REAL_PUBLICATION_PERFORMED=false') }
    elseif ($ReplayFault -ceq 'Failed') { $replayExit = 2 }
    Assert-MockReplayGate $replayLines $replayExit
    $report.replayAssertions = 25
    $validation.Add('mockReplayAndSourceIsolation=PASS_25')
    $contractLines = @(& pwsh -NoProfile -NonInteractive -File $contractTests 2>&1 | ForEach-Object { $_.ToString() })
    if ($LASTEXITCODE -ne 0 -or $contractLines -notcontains 'GATEWAY_RECEIPT_CONTRACTS=PASS' -or
        $contractLines -notcontains 'CONTRACT_ASSERTIONS=15') { throw 'In-memory receipt contract tests failed.' }
    $report.contractAssertionCount = 15
    $validation.Add('inMemoryContract=PASS_15')
    $concurrentLines = @(& pwsh -NoProfile -NonInteractive -File $concurrentGuard 2>&1 | ForEach-Object { $_.ToString() })
    if ($LASTEXITCODE -ne 0 -or $concurrentLines -notcontains 'CONCURRENT_OCCUPIED_PORT_REFUSAL=PASS' -or
        $concurrentLines -notcontains 'CONCURRENT_REFUSAL_COUNT=2') { throw 'Concurrent occupied-port test failed.' }
    foreach ($line in @($concurrentLines | Where-Object { $_ -like 'REFUSAL_RECEIPT=*' })) {
        $name = $line.Substring('REFUSAL_RECEIPT='.Length).Split('\') | Select-Object -Last 1
        if ($name -cnotmatch '^gateway-watchdog-\d{8}-\d{6}-\d{7}-[0-9a-f]{8}\.json$') { throw 'Concurrent receipt name rejected.' }
        $concurrentNames.Add($name)
    }
    if ($concurrentNames.Count -ne 2) { throw 'Expected two distinct concurrent refusals.' }
    $validation.Add('concurrentPortRefusal=PASS_2')
    Invoke-WatchFixture 'FixturePass' 2 10 0
    Invoke-WatchFixture 'FixtureFail' 1 10 2
    Invoke-WatchFixture 'FixtureAbruptExit' 1 10 2
    Invoke-WatchFixture 'FixtureSyntheticAccessViolation' 1 10 2
    Invoke-WatchFixture 'FixtureTimeout' 1 2 2
    $portLines = @(& pwsh -NoProfile -NonInteractive -File $guard 2>&1 | ForEach-Object { $_.ToString() })
    if ($LASTEXITCODE -ne 0 -or @($portLines | Where-Object { $_ -eq 'PORT_OCCUPIED_GUARD=PASS' }).Count -ne 1) {
        throw 'Occupied-port verification did not pass.'
    }
    $portPath = @($portLines | Where-Object { $_ -like 'RECEIPT=*' })
    if ($portPath.Count -ne 1) { throw 'Port guard receipt missing.' }
    $portName = ($portPath[0].Substring(8).Split('\') | Select-Object -Last 1)
    if ($portName -cnotmatch '^gateway-watchdog-\d{8}-\d{6}-\d{7}-[0-9a-f]{8}\.json$') { throw 'Bad port guard receipt.' }
    $portDoc = Get-Content -LiteralPath (Join-Path $root "evidence\builds\$portName") -Raw | ConvertFrom-Json
    if ($portDoc.result -ne 'HALT' -or $portDoc.failureCode -ne 'PORT_BUSY_BEFORE_START' -or @($portDoc.runs).Count -ne 0) {
        throw 'Port guard launched a child or did not fail closed.'
    }
    $watchNames.Add($portName)
    $validation.Add('occupiedPort=REFUSED_BEFORE_CHILD')
    Assert-PortAvailable
    if ($RunLiveGateway) {
        Invoke-WatchFixture 'Gateway' $LiveGatewayIterations 45 0
        Assert-PortAvailable
        $validation.Add('postLivePort=RELEASED')
    }
    Assert-NegativeCall 'pathTraversal' @('-WatchdogReceiptNames','../AGENTS.md','-CoreClrReceiptName',$CoreClrReceiptName)
    Assert-NegativeCall 'duplicateReceipts' @('-WatchdogReceiptNames',("$($watchNames[0]),$($watchNames[0])"),'-CoreClrReceiptName',$CoreClrReceiptName)
    $schemaLines = @(& pwsh -NoProfile -NonInteractive -File $correlator -SelfTestSchemaRejection 2>&1 | ForEach-Object { $_.ToString() })
    if ($LASTEXITCODE -ne 0 -or $schemaLines -notcontains 'MALFORMED_SCHEMA_REJECTED=PASS') { throw 'Malformed schema rejection failed.' }
    $validation.Add('negative:malformedSchema=REJECTED')
    $joined = [string]::Join(',', $watchNames.ToArray())
    $matrixLines = @(& pwsh -NoProfile -NonInteractive -File $correlator -WatchdogReceiptNames $joined -CoreClrReceiptName $CoreClrReceiptName 2>&1 | ForEach-Object { $_.ToString() })
    if ($LASTEXITCODE -ne 0 -or $matrixLines -notcontains 'GATEWAY_CORRELATION=PASS') { throw 'Correlation failed.' }
    $matrixLine = @($matrixLines | Where-Object { $_ -like 'RECEIPT=*' })
    if ($matrixLine.Count -ne 1) { throw 'Correlation receipt missing.' }
    $matrixName = ($matrixLine[0].Substring(8).Split('\') | Select-Object -Last 1)
    if ($matrixName -cnotmatch '^gateway-correlation-\d{8}-\d{6}-\d{7}-[0-9a-f]{8}\.json$') { throw 'Correlation path invalid.' }
    $checkLines = @(& pwsh -NoProfile -NonInteractive -File $validator -CorrelationReceiptName $matrixName -ExpectedLiveGatewayRuns $report.liveGatewayIterations -RequireHistoricalNativeEvent 2>&1 | ForEach-Object { $_.ToString() })
    if ($LASTEXITCODE -ne 0 -or $checkLines -notcontains 'CORRELATION_MATRIX_ASSERTIONS=PASS') { throw 'Correlation matrix assertions failed.' }
    $report.correlationReceiptName = $matrixName
    $report.correlationReceiptSha256 = Get-Sha256 (Join-Path $root "evidence\builds\$matrixName")
    $report.status = 'PASS'
} catch {
    $report.status = 'HALT'
    $report.failureClass = $_.Exception.GetType().FullName
    $report.failure = $_.Exception.Message
} finally {
    $report.watchdogReceiptNames = @($watchNames.ToArray())
    $report.watchdogReceiptSha256 = @($watchNames.ToArray() | ForEach-Object { Get-Sha256 (Join-Path $root "evidence\builds\$_") })
    $report.concurrentRefusalReceiptNames = @($concurrentNames.ToArray())
    $report.concurrentRefusalReceiptSha256 = @($concurrentNames.ToArray() | ForEach-Object { Get-Sha256 (Join-Path $root "evidence\builds\$_") })
    $report.assertions = @($validation.ToArray())
    $report.timestampUtc = (Get-Date).ToUniversalTime().ToString('O')
    $payload = [Text.Encoding]::UTF8.GetBytes(($report | ConvertTo-Json -Depth 8) + [Environment]::NewLine)
    $absolute = Join-Path $root $receipt
    $stream = [IO.File]::Open($absolute,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try { $stream.Write($payload,0,$payload.Length) }
    finally { $stream.Dispose() }
    Write-Output "GATEWAY_OBSERVABILITY=$($report.status)"
    Write-Output "LIVE_GATEWAY_RUNS=$($report.liveGatewayIterations)"
    Write-Output "RECEIPT=$receipt"
    if ($report.status -ne 'PASS') { Write-Output "FAILURE=$($report.failure)" }
}
if ($report.status -ne 'PASS') { exit 2 }
