# Read-only, bounded Windows CoreCLR crash inventory. Never captures memory/dumps.
param(
    [datetime]$StartTime = (Get-Date).AddHours(-4),
    [datetime]$EndTime = (Get-Date)
)
$ErrorActionPreference = 'Stop'
if ($EndTime -le $StartTime -or ($EndTime - $StartTime).TotalHours -gt 24) {
    throw 'Inspection window must be positive and no longer than 24 hours.'
}
Set-Location -LiteralPath 'G:\ProjectName'
. 'G:\ProjectName\eng\gateway-receipt-contract.ps1'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fffffff'
$nonce = ([guid]::NewGuid()).ToString('N').Substring(0, 8)
$receipt = "evidence\builds\coreclr-events-$stamp-$nonce.json"
$runtime = @(& dotnet --list-runtimes)
if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect .NET runtimes.' }
$sdk = @(& dotnet --list-sdks)
if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect .NET SDKs.' }
try {
    $allEvents = @(Get-WinEvent -FilterHashtable @{
            LogName = 'Application'
            StartTime = $StartTime
            EndTime = $EndTime
            Id = @(1000, 1023)
        } -ErrorAction Stop)
} catch {
    if ($_.FullyQualifiedErrorId -like '*NoMatchingEventsFound*') {
        $allEvents = @()
    } else {
        throw
    }
}
$events = @($allEvents | Where-Object {
        $_.ProviderName -in @('.NET Runtime', 'Application Error') -and
        $_.Message -match 'dotnet.exe' -and
        $_.Message -match 'coreclr|0x80131506'
    })
$eventSummaries = @($events | ForEach-Object {
    $message = $_.Message
    # Only explicitly named faulting-process EventData may identify the crashed PID.
    # Event.System.Execution.ProcessID belongs to the reporting provider, not necessarily the faulted process.
    $faultPid = $null
    try {
        [xml]$xml = $_.ToXml()
        $faultPid = Get-ExplicitFaultingPid @($xml.Event.EventData.Data)
    } catch { $faultPid = $null }
    $exceptionCode = if ($message -match '(?i)Exception code:\s*(0x[0-9a-f]+)') {
        $Matches[1].ToLowerInvariant()
    } else { $null }
    $internalCode = if ($message -match '(?i)(0x80131506)') { $Matches[1] } else { $null }
    [ordered]@{
        eventId = $_.Id
        provider = $_.ProviderName
        occurredUtc = $_.TimeCreated.ToUniversalTime().ToString('O')
        module = if ($message -match 'coreclr\.dll') { 'coreclr.dll' } else { 'CoreCLR' }
        exceptionCode = $exceptionCode
        internalCode = $internalCode
        faultingProcessId = $faultPid
        faultingProcessIdSource = if ($null -ne $faultPid) { 'EXPLICIT_NAMED_FAULTING_PROCESS_ID_EVENTDATA' } else { $null }
    }
})
$source = 'G:\ProjectName\src\Services\ProjectName.AgentGateway\Program.cs'
$sha = [Security.Cryptography.SHA256]::Create()
try { $digest = [Convert]::ToHexString($sha.ComputeHash([IO.File]::ReadAllBytes($source))) }
finally { $sha.Dispose() }
$report = [ordered]@{
    schema = 'projectname.coreclr-diagnostics/v1'
    mode = 'READ_ONLY_EVENTS_NO_DUMPS'
    project = 'G:\ProjectName'
    sourceSha256 = $digest
    startUtc = $StartTime.ToUniversalTime().ToString('O')
    endUtc = $EndTime.ToUniversalTime().ToString('O')
    installedNetCoreRuntimes = @($runtime | Where-Object { $_ -match '^Microsoft.NETCore.App ' } | ForEach-Object { ($_ -split '\s+')[1] })
    installedSdkVersions = @($sdk | ForEach-Object { ($_ -split '\s+')[0] })
    matchingEventCount = $eventSummaries.Count
    events = $eventSummaries
    evidenceType = 'LOCAL_OBSERVATION_NOT_INDEPENDENT_CHECKER'
    independentCheckerPass = $false
    productionActivationAllowed = $false
    capturedProcessMemory = $false
    timestampUtc = (Get-Date).ToUniversalTime().ToString('O')
}
$json = $report | ConvertTo-Json -Depth 6
$bytes = [Text.Encoding]::UTF8.GetBytes($json + [Environment]::NewLine)
$receiptAbsolute = Join-Path (Get-Location).ProviderPath $receipt
$stream = [IO.File]::Open($receiptAbsolute, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try { $stream.Write($bytes, 0, $bytes.Length) }
finally { $stream.Dispose() }
Write-Host "CORECLR_EVENT_INSPECTION=PASS"
Write-Host "MATCHED_EVENTS=$($eventSummaries.Count)"
Write-Host "RECEIPT=$receipt"
