# Pure mock-only allowlist and named EventData parser tests; no live event reads.
$ErrorActionPreference = 'Stop'
$root = 'G:\ProjectName'
. (Join-Path $root 'eng\gateway-receipt-contract.ps1')
$manifest = Get-Content -LiteralPath (Join-Path $root 'eng\coreclr-receipts.development.json') -Raw | ConvertFrom-Json
$name = 'coreclr-events-20261029-175538-8528699-65d45e4a.json'
$path = Join-Path $root "evidence\builds\$name"
if (-not [IO.File]::Exists($path)) { throw 'Pinned receipt is missing.' }
$sha = [Security.Cryptography.SHA256]::Create()
try { $actual = [Convert]::ToHexString($sha.ComputeHash([IO.File]::ReadAllBytes($path))) }
finally { $sha.Dispose() }
$doc = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
$now = [datetimeoffset]::UtcNow
Assert-PinnedCoreClrObservation $manifest $doc $name $manifest.receipts[0].sha256 $actual $now
$counter = 1
function Clone([object]$obj) { return ($obj | ConvertTo-Json -Depth 12 | ConvertFrom-Json) }
function Expect-Rejection([string]$case,[scriptblock]$act) {
    $failed=$false
    try { & $act | Out-Null } catch { $failed=$true }
    if (-not $failed) { throw "Unsafe pin accepted: $case." }
    $script:counter++
}
$other = 'coreclr-events-20261029-175538-8528699-aaaaaaaa.json'
Expect-Rejection 'unallowlisted substitution' { Assert-PinnedCoreClrObservation $manifest $doc $other $actual $actual $now }
Expect-Rejection 'missing name' { Assert-PinnedCoreClrObservation $manifest $doc '' $actual $actual $now }
Expect-Rejection 'unmatched supplied digest' { Assert-PinnedCoreClrObservation $manifest $doc $name ('A'*64) $actual $now }
Expect-Rejection 'mutated file digest' { Assert-PinnedCoreClrObservation $manifest $doc $name $actual ('B'*64) $now }
Expect-Rejection 'stale at 25 hours' { Assert-PinnedCoreClrObservation $manifest $doc $name $actual $actual $now.AddHours(25) }
$copy=Clone $manifest
$copy.receipts[0].validUntilUtc = (Convert-StrictUtc $doc.timestampUtc).AddHours(28).ToString('O')
Expect-Rejection 'expiry beyond 24 hours' { Assert-PinnedCoreClrObservation $copy $doc $name $actual $actual $now }
$copy=Clone $manifest
$copy.receipts = @($copy.receipts[0],$copy.receipts[0])
Expect-Rejection 'duplicate allowlist entries' { Assert-PinnedCoreClrObservation $copy $doc $name $actual $actual $now }
$copy=Clone $manifest
$copy.scope='PRODUCTION'
Expect-Rejection 'production claim' { Assert-PinnedCoreClrObservation $copy $doc $name $actual $actual $now }
$copy=Clone $doc
$copy.timestampUtc=$now.AddHours(3).ToString('O')
Expect-Rejection 'future observation' { Assert-PinnedCoreClrObservation $manifest $copy $name $actual $actual $now }
[xml]$named = '<Event><System><Execution ProcessID="444"/></System><EventData><Data Name="FaultingProcessId">0x3E8</Data></EventData></Event>'
if ((Get-ExplicitFaultingPid @($named.Event.EventData.Data)) -ne 1000) { throw 'Explicit named hex PID was not parsed.' }
$counter++
[xml]$decimal = '<Event><EventData><Data Name="FaultingProcessId">1234</Data></EventData></Event>'
if ((Get-ExplicitFaultingPid @($decimal.Event.EventData.Data)) -ne 1234) { throw 'Explicit named decimal PID was not parsed.' }
$counter++
[xml]$provider = '<Event><System><Execution ProcessID="444"/></System><EventData><Data Name="ProcessId">444</Data></EventData></Event>'
if ($null -ne (Get-ExplicitFaultingPid @($provider.Event.EventData.Data))) { throw 'Provider/unnamed process ID was trusted.' }
$counter++
[xml]$unnamed = '<Event><EventData><Data>777</Data></EventData></Event>'
if ($null -ne (Get-ExplicitFaultingPid @($unnamed.Event.EventData.Data))) { throw 'Unnamed PID was trusted.' }
$counter++
[xml]$duplicate = '<Event><EventData><Data Name="FaultingProcessId">100</Data><Data Name="FaultingProcessId">100</Data></EventData></Event>'
if ($null -ne (Get-ExplicitFaultingPid @($duplicate.Event.EventData.Data))) { throw 'Ambiguous duplicate PID was trusted.' }
$counter++
[xml]$zero = '<Event><EventData><Data Name="FaultingProcessId">0x00000000</Data></EventData></Event>'
if ($null -ne (Get-ExplicitFaultingPid @($zero.Event.EventData.Data))) { throw 'Zero PID was trusted.' }
$counter++
[xml]$oversize = '<Event><EventData><Data Name="FaultingProcessId">99999999999</Data></EventData></Event>'
if ($null -ne (Get-ExplicitFaultingPid @($oversize.Event.EventData.Data))) { throw 'Out-of-range PID was trusted.' }
$counter++
Write-Output 'PINNED_CORECLR_CONTRACTS=PASS'
Write-Output "PIN_AND_FIELD_ASSERTIONS=$counter"
