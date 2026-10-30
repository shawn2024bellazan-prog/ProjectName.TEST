# Development-only negative subprocess harness: each fault must HALT before watchdog child.
$ErrorActionPreference='Stop'
$root='G:\ProjectName'
$script=Join-Path $root 'eng\verify-gateway-observability.ps1'
$base=@('-CoreClrReceiptName','coreclr-events-20261029-175538-8528699-65d45e4a.json',
 '-CoreClrReceiptSha256','62A723AD1223DCF48F90158B0B66AA1D3342372341BAE5E11712DBA5775D6F81')
$count=0
foreach($fault in @('Missing','Malformed','Failed')) {
    $lines=@(& pwsh -NoProfile -NonInteractive -File $script @base -ReplayFault $fault 2>&1 | ForEach-Object {$_.ToString()})
    if ($LASTEXITCODE -ne 2 -or @($lines | Where-Object {$_ -ceq 'GATEWAY_OBSERVABILITY=HALT'}).Count -ne 1) {
        throw "Fault $fault did not HALT with exit 2."
    }
    $hit=@($lines|Where-Object {$_ -like 'RECEIPT=*'})
    if($hit.Count -ne 1){throw "Fault $fault has missing parent receipt."}
    $relative=$hit[0].Substring(8)
    if ($relative -cnotmatch '^evidence\\builds\\gateway-observability-\d{8}-\d{6}-\d{7}-[0-9a-f]{8}\.json$') {
        throw "Fault $fault emitted invalid receipt path."
    }
    $data=Get-Content -Raw -LiteralPath (Join-Path $root $relative)|ConvertFrom-Json
    if ($data.status -cne 'HALT' -or $data.replayFault -cne $fault -or
        @($data.watchdogReceiptNames).Count -ne 0 -or
        @($data.concurrentRefusalReceiptNames).Count -ne 0 -or
        $data.workerStarts -ne 0 -or $data.modelCalls -ne 0 -or
        $data.productionActivationAllowed -ne $false -or
        $data.checkerPass -ne $false -or
        $data.failure -notlike '*Mock replay gate denied*') {
        throw "Fault $fault bypassed pre-child denial."
    }
    $count++
    Write-Output "REPLAY_DENIAL_$fault=HALT_PRECHILD"
    Write-Output "NEGATIVE_RECEIPT=$relative"
}
Write-Output 'GATEWAY_REPLAY_DENIALS=PASS'
Write-Output "REPLAY_DENIAL_COUNT=$count"
