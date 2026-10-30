# Read-only self-check for one immutable, redacted development correlation receipt.
param(
    [Parameter(Mandatory=$true)][string]$CorrelationReceiptName,
    [ValidateRange(0,3)][int]$ExpectedLiveGatewayRuns = 0,
    [switch]$RequireHistoricalNativeEvent
)
$ErrorActionPreference = 'Stop'
if ($CorrelationReceiptName -cnotmatch '^gateway-correlation-\d{8}-\d{6}-\d{7}-[0-9a-f]{8}\.json$') { throw 'Bad receipt name.' }
$root = 'G:\ProjectName'
$file = Join-Path $root "evidence\builds\$CorrelationReceiptName"
$doc = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
if ($doc.schema -ne 'projectname.gateway-correlation/v1' -or
    $doc.scope -ne 'LOCAL_OBSERVATION_AND_SYNTHETIC_FIXTURES' -or
    @($doc.results).Count -ne (7 + $ExpectedLiveGatewayRuns)) { throw 'Unexpected matrix shape.' }
if ($RequireHistoricalNativeEvent -and $doc.historicalNativeEvent1000Count -lt 1) { throw 'Missing historical native event.' }
if ($doc.rootCauseProven -ne $false -or $doc.corroboratedChildIdentity -ne $false -or
    $doc.checkerPass -ne $false -or $doc.productionActivationAllowed -ne $false) { throw 'False authority claim.' }
if (@($doc.results | Where-Object { $_.rootCauseProven -ne $false -or $_.receiptSha256 -cnotmatch '^[0-9A-F]{64}$' }).Count -ne 0) {
    throw 'False causality or missing input digest.'
}
$expected = @{
    'FixtureFail'='SYNTHETIC_EXIT_ONLY'
    'FixtureAbruptExit'='SYNTHETIC_EXIT_ONLY'
    'FixtureSyntheticAccessViolation'='SYNTHETIC_EXIT_ONLY'
    'FixtureTimeout'='CHILD_TIMEOUT'
}
foreach ($mode in $expected.Keys) {
    if (@($doc.results | Where-Object { $_.mode -eq $mode -and $_.status -eq $expected[$mode] -and $_.correlation -eq 'NOT_APPLICABLE_SYNTHETIC' }).Count -ne 1) {
        throw "Expected synthetic result for $mode."
    }
}
if (@($doc.results | Where-Object { $_.mode -eq 'FixturePass' -and $_.status -eq 'CLEAN_CHILD_EXIT' -and $_.correlation -eq 'NOT_APPLICABLE_SYNTHETIC' }).Count -ne 2) {
    throw 'Two clean synthetic child exits missing.'
}
if (@($doc.results | Where-Object { $_.status -eq 'PORT_GUARD_REFUSAL' -and $_.correlation -eq 'NO_CHILD_WAS_LAUNCHED' }).Count -ne 1) {
    throw 'Occupied-port guard not represented.'
}
if (@($doc.results | Where-Object { $_.mode -eq 'Gateway' -and $_.status -eq 'CLEAN_CHILD_EXIT' -and $_.correlation -ne 'NOT_APPLICABLE_SYNTHETIC' }).Count -ne $ExpectedLiveGatewayRuns) {
    throw 'Live gateway run count mismatch.'
}
Write-Output 'CORRELATION_MATRIX_ASSERTIONS=PASS'
Write-Output "EXPECTED_LIVE_RUNS=$ExpectedLiveGatewayRuns"
