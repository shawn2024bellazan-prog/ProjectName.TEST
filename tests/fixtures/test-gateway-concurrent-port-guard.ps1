# Two simultaneous watchdogs must refuse a deliberately occupied development port.
# No gateway child is permitted to launch; only exact test PIDs may be stopped.
$ErrorActionPreference = 'Stop'
$root = 'G:\ProjectName'
Set-Location -LiteralPath $root
$watcher = Join-Path $root 'eng\watch-agent-gateway.ps1'
$port = 58440
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$port)
$listener.Server.ExclusiveAddressUse = $true
$children = [Collections.Generic.List[object]]::new()
$receipts = [Collections.Generic.List[string]]::new()
try {
    $listener.Start()
    $nonce = ([guid]::NewGuid()).ToString('N').Substring(0,8)
    foreach ($number in @(1,2)) {
        $stdout = Join-Path $root "evidence\builds\concurrent-port-guard-$nonce-$number.stdout.txt"
        $stderr = Join-Path $root "evidence\builds\concurrent-port-guard-$nonce-$number.stderr.txt"
        $p = Start-Process -FilePath 'pwsh' -ArgumentList @('-NoProfile','-NonInteractive','-File',$watcher,'-Mode','Gateway','-Iterations','1','-TimeoutSeconds','5') -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        $children.Add([pscustomobject]@{ Process=$p; Stdout=$stdout })
    }
    foreach ($item in $children) {
        if (-not $item.Process.WaitForExit(20000)) { throw 'Concurrent port watchdog exceeded 20-second bound.' }
        if ($item.Process.ExitCode -ne 2) { throw "Concurrent port watchdog exit $($item.Process.ExitCode), expected 2." }
        $lines = @(Get-Content -LiteralPath $item.Stdout)
        $matches = @($lines | Where-Object { $_ -like 'GATEWAY_WATCHDOG_RECEIPT=*' })
        if ($matches.Count -ne 1) { throw 'Concurrent watchdog receipt missing.' }
        $relative = $matches[0].Substring('GATEWAY_WATCHDOG_RECEIPT='.Length)
        if ($relative -cnotmatch '^evidence\\builds\\gateway-watchdog-\d{8}-\d{6}-\d{7}-[0-9a-f]{8}\.json$') { throw 'Concurrent watchdog receipt path rejected.' }
        $receipt = Get-Content -LiteralPath (Join-Path $root $relative) -Raw | ConvertFrom-Json
        if ($receipt.result -cne 'HALT' -or $receipt.failureCode -cne 'PORT_BUSY_BEFORE_START' -or
            @($receipt.runs | Where-Object { $null -ne $_ }).Count -ne 0) {
            throw 'Concurrent occupied-port watchdog launched a gateway child.'
        }
        $receipts.Add($relative)
    }
} finally {
    foreach ($item in $children) {
        if (-not $item.Process.HasExited) {
            Stop-Process -Id $item.Process.Id -ErrorAction Stop
            [void]$item.Process.WaitForExit(2000)
        }
        $item.Process.Dispose()
    }
    $listener.Stop()
}
# Validate release by exclusive rebind, never by suppressing Get-NetTCPConnection errors.
$after = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$port)
$after.Server.ExclusiveAddressUse = $true
try { $after.Start() } finally { $after.Stop() }
Write-Output 'CONCURRENT_OCCUPIED_PORT_REFUSAL=PASS'
Write-Output "CONCURRENT_REFUSAL_COUNT=$($receipts.Count)"
foreach($name in $receipts) { Write-Output "REFUSAL_RECEIPT=$name" }
