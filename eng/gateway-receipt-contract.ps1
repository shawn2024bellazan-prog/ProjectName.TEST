# Pure validation and redacted-correlation contracts; no I/O, grants, or authority.
function Assert-ReceiptSchema([object]$document, [string]$expected) {
    if ($null -eq $document -or $document.schema -cne $expected) { throw 'Receipt schema mismatch.' }
}
function Assert-RequiredFields([object]$document, [string[]]$fields) {
    if ($null -eq $document) { throw 'Receipt missing required fields.' }
    foreach ($field in $fields) {
        if (@($document.PSObject.Properties | Where-Object { $_.Name -ceq $field }).Count -ne 1) {
            throw "Receipt missing required field: $field."
        }
    }
}
function Test-IntegerNumber([object]$value) {
    return ($value -is [int] -or $value -is [long])
}
function Convert-StrictUtc([object]$value) {
    # PowerShell 7 ConvertFrom-Json materializes ISO timestamps as DateTime;
    # Windows PowerShell 5.1 leaves them as strings. Reject unspecified Kind.
    if ($value -is [datetimeoffset]) { return $value }
    if ($value -is [datetime]) {
        if ($value.Kind -eq [DateTimeKind]::Unspecified) { throw 'Timestamp lost offset.' }
        return [datetimeoffset]::new($value)
    }
    if ($value -isnot [string] -or $value -cnotmatch '(Z|[+-]\d{2}:\d{2})$') { throw 'Timestamp must be offset-bound UTC.' }
    $parsed = [datetimeoffset]::MinValue
    if (-not [datetimeoffset]::TryParse($value,[cultureinfo]::InvariantCulture,[Globalization.DateTimeStyles]::None,[ref]$parsed)) {
        throw 'Timestamp invalid.'
    }
    return $parsed
}
function Assert-GatewayWatchdogReceipt([object]$data) {
    Assert-ReceiptSchema $data 'projectname.gateway-watchdog/v1'
    Assert-RequiredFields $data @('mode','scope','maxIterations','childTimeoutSeconds','result','failureCode','runs','checkerPass','productionActivationAllowed')
    if ($data.scope -cne 'ISOLATED_DEVELOPMENT_ONLY' -or
        $data.checkerPass -isnot [bool] -or $data.checkerPass -ne $false -or
        $data.productionActivationAllowed -isnot [bool] -or $data.productionActivationAllowed -ne $false) {
        throw 'Watchdog authority/scope invariants violated.'
    }
    if ($data.mode -cnotin @('Gateway','FixturePass','FixtureFail','FixtureAbruptExit','FixtureSyntheticAccessViolation','FixtureTimeout') -or
        $data.result -cnotin @('PASS','LOOP','HALT') -or
        -not (Test-IntegerNumber $data.maxIterations) -or $data.maxIterations -lt 1 -or $data.maxIterations -gt 3 -or
        -not (Test-IntegerNumber $data.childTimeoutSeconds) -or $data.childTimeoutSeconds -lt 2 -or $data.childTimeoutSeconds -gt 90 -or
        $null -eq $data.runs -or $data.runs -isnot [array]) {
        throw 'Watchdog bounded structure invalid.'
    }
    $runs = @($data.runs | Where-Object { $null -ne $_ })
    if ($runs.Count -gt $data.maxIterations) { throw 'Watchdog exceeds iteration cap.' }
    foreach ($run in $runs) {
        Assert-RequiredFields $run @('iteration','childProcessId','status','startedUtc','finishedUtc','elapsedMs','exitCodeHex','portReleased')
        if (-not (Test-IntegerNumber $run.iteration) -or $run.iteration -lt 1 -or $run.iteration -gt $data.maxIterations -or
            -not (Test-IntegerNumber $run.childProcessId) -or $run.childProcessId -le 0 -or
            $run.status -cnotin @('PASS','FAILED_EXIT','TIMEOUT','HALT_ORPHAN_LISTENER','HARNESS_EXCEPTION') -or
            $run.elapsedMs -lt 0 -or $run.elapsedMs -gt (($data.childTimeoutSeconds + 15) * 1000)) {
            throw 'Watchdog child structure invalid.'
        }
        $started = Convert-StrictUtc $run.startedUtc
        $finished = Convert-StrictUtc $run.finishedUtc
        if ($finished -lt $started) { throw 'Watchdog run time reversed.' }
        if ($data.mode -ceq 'Gateway' -and $run.portReleased -ne $true) {
            throw 'Gateway port release not established.'
        }
    }
    if ($data.failureCode -ceq 'PORT_BUSY_BEFORE_START') {
        if ($data.mode -cne 'Gateway' -or $data.result -cne 'HALT' -or $runs.Count -ne 0) {
            throw 'Port refusal must halt before child start.'
        }
    } elseif ($data.result -ceq 'PASS') {
        if ($runs.Count -ne $data.maxIterations -or @($runs | Where-Object {$_.status -cne 'PASS'}).Count -ne 0) {
            throw 'Watchdog PASS inconsistent with runs.'
        }
    } elseif ($data.result -ceq 'LOOP') {
        if ($runs.Count -eq 0 -or @($runs | Where-Object {$_.status -cne 'PASS'}).Count -eq 0) {
            throw 'Watchdog LOOP inconsistent with runs.'
        }
    }
}
function Assert-RedactedCoreClrReceipt([object]$data) {
    Assert-ReceiptSchema $data 'projectname.coreclr-diagnostics/v1'
    Assert-RequiredFields $data @('mode','evidenceType','sourceSha256','startUtc','endUtc','matchingEventCount','events','independentCheckerPass','productionActivationAllowed','capturedProcessMemory')
    if ($data.evidenceType -cne 'LOCAL_OBSERVATION_NOT_INDEPENDENT_CHECKER' -or
        $data.mode -cne 'READ_ONLY_EVENTS_NO_DUMPS' -or
        $data.independentCheckerPass -ne $false -or $data.productionActivationAllowed -ne $false -or
        $data.capturedProcessMemory -ne $false -or $data.sourceSha256 -cnotmatch '^[A-F0-9]{64}$' -or
        $null -eq $data.events -or $data.events -isnot [array]) {
        throw 'CoreCLR receipt scope or structure invalid.'
    }
    $events = @($data.events | Where-Object { $null -ne $_ })
    if ($data.matchingEventCount -ne $events.Count -or $events.Count -gt 256) { throw 'CoreCLR event count invalid.' }
    $start = Convert-StrictUtc $data.startUtc
    $end = Convert-StrictUtc $data.endUtc
    if ($end -le $start -or ($end-$start).TotalHours -gt 24) { throw 'CoreCLR inspection window invalid.' }
    foreach ($event in $events) {
        Assert-RequiredFields $event @('eventId','provider','occurredUtc','module','exceptionCode','internalCode')
        $occurred = Convert-StrictUtc $event.occurredUtc
        if ($occurred -lt $start.AddMinutes(-1) -or $occurred -gt $end.AddMinutes(1)) { throw 'CoreCLR event outside inspection window.' }
        if ($event.PSObject.Properties['faultingProcessId'] -and $null -ne $event.faultingProcessId) {
            if (-not (Test-IntegerNumber $event.faultingProcessId) -or $event.faultingProcessId -le 0 -or
                $event.faultingProcessIdSource -cne 'EXPLICIT_NAMED_FAULTING_PROCESS_ID_EVENTDATA') {
                throw 'Untrusted event process identity.'
            }
        }
    }
}
function Get-RedactedEventCorrelation([object]$run,[object[]]$eventSummaries,[bool]$isFixture) {
    if ($isFixture) { return 'NOT_APPLICABLE_SYNTHETIC' }
    $started = Convert-StrictUtc $run.startedUtc
    $finished = Convert-StrictUtc $run.finishedUtc
    if ($finished -lt $started) { throw 'Run timestamps reversed.' }
    $start = $started.AddSeconds(-2)
    $end = $finished.AddSeconds(2)
    $overlap = @($eventSummaries | Where-Object {
        $time = Convert-StrictUtc $_.occurredUtc
        $time -ge $start -and $time -le $end
    })
    if ($overlap.Count -eq 0) { return 'NO_PID_BOUND_EVENT' }
    $pidEvidence = @($overlap | Where-Object {
        $null -ne $_.faultingProcessId -and
        $_.faultingProcessIdSource -ceq 'EXPLICIT_NAMED_FAULTING_PROCESS_ID_EVENTDATA'
    })
    $matched = @($pidEvidence | Where-Object { $_.faultingProcessId -eq $run.childProcessId })
    if ($matched.Count -gt 1) { return 'AMBIGUOUS_PID_TIME_METADATA' }
    if ($matched.Count -eq 1) { return 'PID_TIME_METADATA_CANDIDATE_NOT_CAUSAL' }
    if ($pidEvidence.Count -gt 0) { return 'TEMPORAL_ONLY_PID_MISMATCH' }
    return 'TEMPORAL_ONLY_NO_PROCESS_BINDING'
}

# XML EventData only; never uses provider PID or unnamed application payloads.
function Get-ExplicitFaultingPid([object[]]$eventDataNodes) {
    $matching = @($eventDataNodes | Where-Object {
        $_ -is [Xml.XmlElement] -and $_.GetAttribute('Name') -ceq 'FaultingProcessId'
    })
    if ($matching.Count -ne 1) { return $null }
    $raw = [string]$matching[0].InnerText
    $number = 0
    $style = [Globalization.NumberStyles]::None
    if ($raw -cmatch '^0x[0-9A-Fa-f]{1,8}$') {
        $raw = $raw.Substring(2)
        $style = [Globalization.NumberStyles]::AllowHexSpecifier
    } elseif ($raw -cnotmatch '^[0-9]{1,10}$') { return $null }
    if (-not [int]::TryParse($raw,$style,[cultureinfo]::InvariantCulture,[ref]$number) -or $number -le 0) {
        return $null
    }
    return $number
}
function Assert-PinnedCoreClrObservation(
    [object]$manifest,[object]$document,[string]$receiptName,
    [string]$suppliedSha256,[string]$actualSha256,[datetimeoffset]$nowUtc
) {
    Assert-RequiredFields $manifest @('schema','scope','receipts','checkerPass','productionActivationAllowed')
    if ($manifest.schema -cne 'projectname.redacted-event-allowlist/v1' -or
        $manifest.scope -cne 'ISOLATED_DEVELOPMENT_ONLY' -or
        $manifest.checkerPass -ne $false -or $manifest.productionActivationAllowed -ne $false -or
        $null -eq $manifest.receipts -or $manifest.receipts -isnot [array]) {
        throw 'Event allowlist structure rejected.'
    }
    if ($receiptName -cnotmatch '^coreclr-events-\d{8}-\d{6}-\d{7}-[0-9a-f]{8}\.json$' -or
        $suppliedSha256 -cnotmatch '^[0-9A-F]{64}$' -or
        $actualSha256 -cnotmatch '^[0-9A-F]{64}$') { throw 'Pinned receipt name or hash invalid.' }
    $pins = @($manifest.receipts | Where-Object { $_.name -ceq $receiptName })
    if ($pins.Count -ne 1 -or $manifest.receipts.Count -lt 1 -or $manifest.receipts.Count -gt 10) {
        throw 'Receipt not uniquely allowlisted.'
    }
    if ($pins[0].sha256 -cne $suppliedSha256 -or $actualSha256 -cne $suppliedSha256) {
        throw 'Pinned receipt digest mismatch.'
    }
    Assert-RedactedCoreClrReceipt $document
    $observed = Convert-StrictUtc $document.timestampUtc
    $validUntil = Convert-StrictUtc $pins[0].validUntilUtc
    if ($observed -gt $nowUtc.AddMinutes(2) -or
        $nowUtc -gt $observed.AddHours(24) -or
        $nowUtc -gt $validUntil -or
        $validUntil -gt $observed.AddHours(24)) {
        throw 'Pinned redacted event observation stale or future-dated.'
    }
}
