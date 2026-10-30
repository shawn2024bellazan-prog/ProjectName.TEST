# Development-only correlation of immutable watchdog and redacted CoreCLR receipts.
# Does not assert a shared root cause without process identity binding.
param(
    [string]$WatchdogReceiptNames,
    [string]$CoreClrReceiptName,
    [switch]$SelfTestSchemaRejection
)
$ErrorActionPreference = 'Stop'
$root = 'G:\ProjectName'
Set-Location -LiteralPath $root
$evidenceRoot = Join-Path $root 'evidence\builds'
. (Join-Path $root 'eng\gateway-receipt-contract.ps1')
if ($SelfTestSchemaRejection) {
    try {
        Assert-ReceiptSchema ([pscustomobject]@{ schema='untrusted.test/v0' }) 'projectname.gateway-watchdog/v1'
        throw 'Malformed schema unexpectedly accepted.'
    } catch {
        if ($_.Exception.Message -ne 'Receipt schema mismatch.') { throw }
    }
    Write-Output 'MALFORMED_SCHEMA_REJECTED=PASS'
    exit 0
}
if ([string]::IsNullOrWhiteSpace($WatchdogReceiptNames) -or [string]::IsNullOrWhiteSpace($CoreClrReceiptName)) {
    throw 'Both receipt name arguments are required.'
}
$watchNames = @($WatchdogReceiptNames.Split(',') | ForEach-Object { $_.Trim() })
if ($watchNames.Count -lt 1 -or $watchNames.Count -gt 10 -or
    @($watchNames | Select-Object -Unique).Count -ne $watchNames.Count) {
    throw 'Provide 1–10 distinct watchdog receipt basenames.'
}
function Read-BoundReceipt([string]$name, [string]$pattern, [string]$schema) {
    if ($name -cnotmatch $pattern) { throw 'Receipt basename rejected.' }
    $path = Join-Path $evidenceRoot $name
    $item = Get-Item -LiteralPath $path -ErrorAction Stop
    if ($item.Length -le 0 -or $item.Length -gt 1048576) { throw 'Receipt size outside cap.' }
    $json = Get-Content -LiteralPath $path -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    Assert-ReceiptSchema $json $schema
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [Convert]::ToHexString($sha.ComputeHash([IO.File]::ReadAllBytes($path))) }
    finally { $sha.Dispose() }
    return [pscustomobject]@{ Data = $json; Hash = $hash; Name = $name }
}
$watchPattern = '^gateway-watchdog-\d{8}-\d{6}-\d{7}-[0-9a-f]{8}\.json$'
$eventPattern = '^coreclr-events-\d{8}-\d{6}-\d{7}-[0-9a-f]{8}\.json$'
$core = Read-BoundReceipt $CoreClrReceiptName $eventPattern 'projectname.coreclr-diagnostics/v1'
Assert-RedactedCoreClrReceipt $core.Data
if ($core.Data.evidenceType -ne 'LOCAL_OBSERVATION_NOT_INDEPENDENT_CHECKER' -or
    $core.Data.independentCheckerPass -ne $false -or $core.Data.productionActivationAllowed -ne $false) {
    throw 'CoreCLR observation is not eligible for local correlation.'
}
$eventSummaries = @($core.Data.events | Where-Object { $null -ne $_ })
$rows = [Collections.Generic.List[object]]::new()
foreach ($name in $watchNames) {
    $receipt = Read-BoundReceipt $name $watchPattern 'projectname.gateway-watchdog/v1'
    $data = $receipt.Data
    Assert-GatewayWatchdogReceipt $data
    if ($data.scope -ne 'ISOLATED_DEVELOPMENT_ONLY' -or
        $data.checkerPass -ne $false -or $data.productionActivationAllowed -ne $false) {
        throw 'Watchdog receipt violates development-only invariants.'
    }
    if ($data.failureCode -eq 'PORT_BUSY_BEFORE_START') {
        if (@($data.runs | Where-Object { $null -ne $_ }).Count -ne 0) {
            throw 'Occupied-port refusal must not report launched children.'
        }
        $rows.Add([pscustomobject][ordered]@{
            receiptName = $name; receiptSha256 = $receipt.Hash
            mode = $data.mode; iteration = $null; status = 'PORT_GUARD_REFUSAL'
            exitCodeHex = $null; correlation = 'NO_CHILD_WAS_LAUNCHED'
            rootCauseProven = $false
        })
    }
    foreach ($run in @($data.runs | Where-Object { $null -ne $_ })) {
        $isFixture = $data.mode -like 'Fixture*'
        $status = switch ($run.status) {
            'PASS' { 'CLEAN_CHILD_EXIT' }
            'TIMEOUT' { 'CHILD_TIMEOUT' }
            'HALT_ORPHAN_LISTENER' { 'ORPHAN_LISTENER_HALT' }
            'FAILED_EXIT' { if ($isFixture) { 'SYNTHETIC_EXIT_ONLY' } else { 'NONZERO_EXIT_UNATTRIBUTED' } }
            default { 'HARNESS_FAILURE' }
        }
        $correlation = Get-RedactedEventCorrelation $run $eventSummaries $isFixture
        $rows.Add([pscustomobject][ordered]@{
            receiptName = $name; receiptSha256 = $receipt.Hash
            mode = $data.mode; iteration = $run.iteration; status = $status
            exitCodeHex = $run.exitCodeHex; correlation = $correlation
            rootCauseProven = $false
        })
    }
}
$nativeEvents = @($eventSummaries | Where-Object {
    $_.eventId -eq 1000 -and $_.module -eq 'coreclr.dll' -and $_.exceptionCode -eq '0xc0000005'
})
$report = [ordered]@{
    schema = 'projectname.gateway-correlation/v1'
    scope = 'LOCAL_OBSERVATION_AND_SYNTHETIC_FIXTURES'
    coreClrReceipt = $core.Name
    coreClrReceiptSha256 = $core.Hash
    historicalNativeEvent1000Count = $nativeEvents.Count
    corroboratedChildIdentity = $false
    rootCauseProven = $false
    checkerPass = $false
    productionActivationAllowed = $false
    results = @($rows.ToArray())
    timestampUtc = (Get-Date).ToUniversalTime().ToString('O')
}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fffffff'
$nonce = ([guid]::NewGuid()).ToString('N').Substring(0,8)
$outputName = "gateway-correlation-$stamp-$nonce.json"
$absolute = Join-Path $evidenceRoot $outputName
$bytes = [Text.Encoding]::UTF8.GetBytes(($report | ConvertTo-Json -Depth 7) + [Environment]::NewLine)
$stream = [IO.File]::Open($absolute, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try { $stream.Write($bytes, 0, $bytes.Length) }
finally { $stream.Dispose() }
Write-Output "GATEWAY_CORRELATION=PASS"
Write-Output "CORRELATION_ROWS=$($rows.Count)"
Write-Output "ROOT_CAUSE_PROVEN=false"
Write-Output "RECEIPT=evidence\builds\$outputName"

