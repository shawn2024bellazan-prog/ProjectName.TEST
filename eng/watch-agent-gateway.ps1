# Parent-owned receipt for isolated AgentGateway smoke and synthetic watchdog fixtures.
# Do not start Aspire, workers, models, or native agent routes.
param(
    [ValidateSet('Gateway','FixturePass','FixtureFail','FixtureAbruptExit','FixtureSyntheticAccessViolation','FixtureTimeout')]
    [string]$Mode = 'Gateway',
    [ValidateRange(1,3)][int]$Iterations = 1,
    [ValidateRange(2,90)][int]$TimeoutSeconds = 45
)
$ErrorActionPreference = 'Stop'
$root = 'G:\ProjectName'
Set-Location -LiteralPath $root
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fffffff'
$nonce = ([guid]::NewGuid()).ToString('N').Substring(0,8)
$base = "gateway-watchdog-$stamp-$nonce"
$receipt = "evidence\builds\$base.json"
$gatewayScript = Join-Path $root 'eng\smoke-agent-gateway.ps1'
$fixtureScript = Join-Path $root 'tests\fixtures\watchdog-child.ps1'
$childPath = if ($Mode -eq 'Gateway') { $gatewayScript } else { $fixtureScript }
$fixtureMode = switch ($Mode) {
    'FixturePass' { 'Pass' }
    'FixtureFail' { 'Fail' }
    'FixtureAbruptExit' { 'AbruptExit' }
    'FixtureSyntheticAccessViolation' { 'SyntheticAccessViolation' }
    'FixtureTimeout' { 'Timeout' }
    default { $null }
}
function Test-IsolatedGatewayPortAvailable {
    $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 58440)
    $probe.Server.ExclusiveAddressUse = $true
    try {
        $probe.Start()
        return $true
    } catch {
        $cause = $_.Exception
        while ($null -ne $cause.InnerException) { $cause = $cause.InnerException }
        if ($cause -is [Net.Sockets.SocketException] -and
            $cause.SocketErrorCode -eq [Net.Sockets.SocketError]::AddressAlreadyInUse) {
            return $false
        }
        throw
    } finally {
        $probe.Stop()
    }
}
$rows = [Collections.Generic.List[object]]::new()
$report = [ordered]@{
    schema = 'projectname.gateway-watchdog/v1'
    mode = $Mode
    scope = 'ISOLATED_DEVELOPMENT_ONLY'
    maxIterations = $Iterations
    childTimeoutSeconds = $TimeoutSeconds
    result = 'NOT_STARTED'
    failureCode = $null
    events = $null
    runs = $null
    childProcessMemoryCaptured = $false
    checkerPass = $false
    productionActivationAllowed = $false
}
try {
    if (-not (Test-Path -LiteralPath $childPath -PathType Leaf)) { throw 'Child script missing' }
    for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
        if ($Mode -eq 'Gateway') {
            if (-not (Test-IsolatedGatewayPortAvailable)) {
                $report.failureCode = 'PORT_BUSY_BEFORE_START'
                throw 'Gateway test port already occupied before child start'
            }
        }
        $stdout = "evidence\builds\$base-run$iteration.stdout.txt"
        $stderr = "evidence\builds\$base-run$iteration.stderr.txt"
        $args = @('-NoProfile','-NonInteractive','-File',$childPath)
        if ($fixtureMode) { $args += @('-Mode',$fixtureMode) }
        $record = [ordered]@{
            iteration = $iteration
            childProcessId = $null
            exitCodeSigned = $null
            exitCodeHex = $null
            status = 'NOT_STARTED'
            startedUtc = (Get-Date).ToUniversalTime().ToString('O')
            finishedUtc = $null
            elapsedMs = 0
            stdoutFile = $stdout
            stderrFile = $stderr
            portReleased = $null
            errorClass = $null
        }
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $child = $null
        try {
            $child = Start-Process -FilePath 'pwsh' -ArgumentList $args -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
            $record.childProcessId = $child.Id
            $finished = $child.WaitForExit($TimeoutSeconds * 1000)
            if ($finished) {
                $code = [int]$child.ExitCode
                $record.exitCodeSigned = $code
                $record.exitCodeHex = '0x' + ([uint32]([int64]$code -band 0xffffffffL)).ToString('X8')
                $record.status = if ($code -eq 0) { 'PASS' } else { 'FAILED_EXIT' }
            } else {
                $record.status = 'TIMEOUT'
                Stop-Process -Id $child.Id -ErrorAction Stop
                [void]$child.WaitForExit(2000)
                if ($child.HasExited) {
                    $code = [int]$child.ExitCode
                    $record.exitCodeSigned = $code
                    $record.exitCodeHex = '0x' + ([uint32]([int64]$code -band 0xffffffffL)).ToString('X8')
                }
            }
        } catch {
            $record.status = 'HARNESS_EXCEPTION'
            $record.errorClass = $_.Exception.GetType().FullName
        } finally {
            $timer.Stop()
            $record.elapsedMs = [int]$timer.ElapsedMilliseconds
            $record.finishedUtc = (Get-Date).ToUniversalTime().ToString('O')
            if ($null -ne $child) { $child.Dispose() }
        }
        if ($Mode -eq 'Gateway') {
            Start-Sleep -Milliseconds 500
            $record.portReleased = Test-IsolatedGatewayPortAvailable
            if (-not $record.portReleased) { $record.status = 'HALT_ORPHAN_LISTENER' }
        }
        $rows.Add([pscustomobject]$record)
        if ($record.status -ne 'PASS') { break }
    }
    $orphanDetected = @($rows | Where-Object { $_.status -eq 'HALT_ORPHAN_LISTENER' }).Count -gt 0
    $report.result = if ($orphanDetected) { 'HALT' } elseif ($rows.Count -eq $Iterations -and @($rows | Where-Object { $_.status -ne 'PASS' }).Count -eq 0) { 'PASS' } else { 'LOOP' }
} catch {
    $report.result = 'HALT'
    $report.errorClass = $_.Exception.GetType().FullName
} finally {
    $report.runs = @($rows.ToArray())
    $report.timestampUtc = (Get-Date).ToUniversalTime().ToString('O')
    $json = $report | ConvertTo-Json -Depth 7
    $bytes = [Text.Encoding]::UTF8.GetBytes($json + [Environment]::NewLine)
    $absolute = Join-Path $root $receipt
    $stream = [IO.File]::Open($absolute, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($bytes, 0, $bytes.Length) }
    finally { $stream.Dispose() }
    Write-Host "GATEWAY_WATCHDOG_RESULT=$($report.result)"
    Write-Host "GATEWAY_WATCHDOG_RUNS=$($rows.Count)"
    Write-Host "GATEWAY_WATCHDOG_RECEIPT=$receipt"
}
if ($report.result -ne 'PASS') { exit 2 }

