# In-memory negative contract tests; never fabricate evidence receipts.
$ErrorActionPreference = 'Stop'
$root = 'G:\ProjectName'
. (Join-Path $root 'eng\gateway-receipt-contract.ps1')
$good = Get-Content -LiteralPath (Join-Path $root 'evidence\builds\gateway-watchdog-20261029-174300-0924805-9099badf.json') -Raw | ConvertFrom-Json
$core = Get-Content -LiteralPath (Join-Path $root 'evidence\builds\coreclr-events-20261029-151429-6185131-df830153.json') -Raw | ConvertFrom-Json
Assert-GatewayWatchdogReceipt $good
Assert-RedactedCoreClrReceipt $core
$passes = 0
function Copy-Object([object]$source) { return ($source | ConvertTo-Json -Depth 10 | ConvertFrom-Json) }
function Expect-Rejection([string]$label,[scriptblock]$action) {
    $rejected = $false
    try { & $action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw "Contract unexpectedly accepted $label." }
    $script:passes++
}
$bad = Copy-Object $good
$bad.PSObject.Properties.Remove('runs')
Expect-Rejection 'missing runs' { Assert-GatewayWatchdogReceipt $bad }
$bad = Copy-Object $good
$bad.schema = 'unknown.v0'
Expect-Rejection 'bad schema' { Assert-GatewayWatchdogReceipt $bad }
$bad = Copy-Object $good
$bad.maxIterations = 9
Expect-Rejection 'unbounded iterations' { Assert-GatewayWatchdogReceipt $bad }
$bad = Copy-Object $good
$bad.runs[0].status = 'FAILED_EXIT'
Expect-Rejection 'forged PASS' { Assert-GatewayWatchdogReceipt $bad }
$bad = Copy-Object $good
$bad.runs[0].finishedUtc = (Convert-StrictUtc $bad.runs[0].startedUtc).AddSeconds(-1).ToString('O')
Expect-Rejection 'reversed run timestamps' { Assert-GatewayWatchdogReceipt $bad }
$bad = Copy-Object $good
$bad.runs[0].startedUtc = 'not-an-instant'
Expect-Rejection 'invalid run timestamp' { Assert-GatewayWatchdogReceipt $bad }
$bad = Copy-Object $core
$bad.PSObject.Properties.Remove('events')
Expect-Rejection 'missing event array' { Assert-RedactedCoreClrReceipt $bad }
$bad = Copy-Object $core
$bad.matchingEventCount = 199
Expect-Rejection 'event count mismatch' { Assert-RedactedCoreClrReceipt $bad }
$bad = Copy-Object $core
if (@($bad.events).Count -gt 0) {
    $bad.events[0].occurredUtc = ([datetimeoffset]::Parse($bad.endUtc).AddDays(2)).ToString('O')
    Expect-Rejection 'disputed event timestamp' { Assert-RedactedCoreClrReceipt $bad }
}
$run = $good.runs[0]
$at = (Convert-StrictUtc $run.startedUtc).AddMilliseconds(100).ToString('O')
$event = [pscustomobject]@{ occurredUtc=$at; faultingProcessId=$null; faultingProcessIdSource=$null }
if ((Get-RedactedEventCorrelation $run @($event) $false) -cne 'TEMPORAL_ONLY_NO_PROCESS_BINDING') { throw 'Missing PID not treated as temporal-only.' }
$event.faultingProcessId = [int]($run.childProcessId + 1)
$event.faultingProcessIdSource = 'EXPLICIT_NAMED_FAULTING_PROCESS_ID_EVENTDATA'
if ((Get-RedactedEventCorrelation $run @($event) $false) -cne 'TEMPORAL_ONLY_PID_MISMATCH') { throw 'Mismatched PID not rejected.' }
$event.faultingProcessId = [int]$run.childProcessId
if ((Get-RedactedEventCorrelation $run @($event) $false) -cne 'PID_TIME_METADATA_CANDIDATE_NOT_CAUSAL') { throw 'Metadata-only PID match classification failed.' }
if ((Get-RedactedEventCorrelation $run @($event,$event) $false) -cne 'AMBIGUOUS_PID_TIME_METADATA') { throw 'Ambiguous event PID not classified.' }
if ((Get-RedactedEventCorrelation $run @($event) $true) -cne 'NOT_APPLICABLE_SYNTHETIC') { throw 'Synthetic must not be correlated.' }
$event.occurredUtc='invalid'
Expect-Rejection 'malformed correlation event timestamp' { Get-RedactedEventCorrelation $run @($event) $false }
$passes += 5
Write-Output 'GATEWAY_RECEIPT_CONTRACTS=PASS'
Write-Output "CONTRACT_ASSERTIONS=$passes"
