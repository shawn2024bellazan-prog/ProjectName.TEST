# Isolated loopback reservation proving gateway watchdog refuses an occupied port.
$ErrorActionPreference = 'Stop'
$root = 'G:\ProjectName'
$port = 58440
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $port)
$listener.Server.ExclusiveAddressUse = $true
try {
    $listener.Start()
    $lines = @(& pwsh -NoProfile -File (Join-Path $root 'eng\watch-agent-gateway.ps1') -Mode Gateway -Iterations 1 -TimeoutSeconds 5)
    if ($LASTEXITCODE -ne 2) { throw 'Expected occupied-port HALT exit 2.' }
    $receiptLine = @($lines | Where-Object { $_ -like 'GATEWAY_WATCHDOG_RECEIPT=*' })
    if ($receiptLine.Count -ne 1) { throw 'Expected one parent-owned receipt path.' }
    $relative = $receiptLine[0].Substring('GATEWAY_WATCHDOG_RECEIPT='.Length)
    $receipt = Get-Content -LiteralPath (Join-Path $root $relative) -Raw | ConvertFrom-Json
    if ($receipt.result -ne 'HALT' -or $receipt.failureCode -ne 'PORT_BUSY_BEFORE_START' -or @($receipt.runs).Count -ne 0) {
        throw 'Port guard did not refuse before child creation.'
    }
    Write-Output 'PORT_OCCUPIED_GUARD=PASS'
    Write-Output "RECEIPT=$relative"
} finally {
    $listener.Stop()
}
