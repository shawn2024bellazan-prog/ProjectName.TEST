# Pure development-only proposal contract. No filesystem writes or approval route.
# A checked proposal is not an active allowlist or an independent Checker verdict.
function Assert-CoreClrRotationProposal(
    [object]$active,[object]$proposal,[object]$observation,
    [string]$actualActiveSha256,[string]$actualObservationSha256,
    [datetimeoffset]$nowUtc,[int]$currentRevision
) {
    Assert-RequiredFields $active @('schema','scope','receipts','checkerPass','productionActivationAllowed')
    Assert-RequiredFields $proposal @('schema','scope','state','previousRevision','proposedRevision',
        'previousManifestSha256','previousReceiptName','previousReceiptSha256',
        'replacementReceipt','approvalRecord','checkerPass','productionActivationAllowed')
    if ($active.schema -cne 'projectname.redacted-event-allowlist/v1' -or
        $active.scope -cne 'ISOLATED_DEVELOPMENT_ONLY' -or
        $active.checkerPass -cne $false -or $active.productionActivationAllowed -cne $false -or
        $active.receipts -isnot [array] -or $active.receipts.Count -ne 1) {
        throw 'Active development allowlist contract changed.'
    }
    if ($proposal.schema -cne 'projectname.redacted-event-rotation-proposal/v1' -or
        $proposal.scope -cne 'ISOLATED_DEVELOPMENT_ONLY' -or
        $proposal.state -cne 'PROPOSED_UNEXECUTED' -or
        $proposal.checkerPass -isnot [bool] -or $proposal.checkerPass -ne $false -or
        $proposal.productionActivationAllowed -isnot [bool] -or $proposal.productionActivationAllowed -ne $false -or
        $null -ne $proposal.approvalRecord -or
        $null -ne $proposal.PSObject.Properties['approved']) {
        throw 'Rotation proposal claims activation or unverified approval.'
    }
    if ($currentRevision -lt 1 -or
        -not (Test-IntegerNumber $proposal.previousRevision) -or
        -not (Test-IntegerNumber $proposal.proposedRevision) -or
        $proposal.previousRevision -ne $currentRevision -or
        $proposal.proposedRevision -ne ($currentRevision + 1)) {
        throw 'Monotonic revision/CAS precondition failed.'
    }
    if ($actualActiveSha256 -cnotmatch '^[A-F0-9]{64}$' -or
        $proposal.previousManifestSha256 -cne $actualActiveSha256 -or
        $proposal.previousReceiptName -cne $active.receipts[0].name -or
        $proposal.previousReceiptSha256 -cne $active.receipts[0].sha256) {
        throw 'Source manifest/receipt rollback or substitution detected.'
    }
    $candidate = $proposal.replacementReceipt
    Assert-RequiredFields $candidate @('name','sha256','validUntilUtc')
    if ($candidate.name -ceq $proposal.previousReceiptName -or
        $candidate.sha256 -ceq $proposal.previousReceiptSha256 -or
        $candidate.name -cnotmatch '^coreclr-events-\d{8}-\d{6}-\d{7}-[0-9a-f]{8}\.json$' -or
        $candidate.sha256 -cnotmatch '^[A-F0-9]{64}$' -or
        $actualObservationSha256 -cne $candidate.sha256) {
        throw 'Duplicate, malformed, or substituted observation.'
    }
    Assert-RedactedCoreClrReceipt $observation
    $observedAt = Convert-StrictUtc $observation.timestampUtc
    $validUntil = Convert-StrictUtc $candidate.validUntilUtc
    $activeExpires = Convert-StrictUtc $active.receipts[0].validUntilUtc
    if ($observedAt -gt $nowUtc.AddMinutes(2) -or
        $nowUtc -gt $observedAt.AddHours(24) -or
        $nowUtc -gt $validUntil -or
        $validUntil -gt $observedAt.AddHours(24) -or
        $validUntil -le $nowUtc -or
        $validUntil -le $activeExpires) {
        throw 'Proposed observation is expired, future, or non-monotonic.'
    }
}
function Assert-CoreClrRotationNotApproved([object]$proposal) {
    # There is intentionally no activation/promotion code path. An approval flag is not a signature.
    if ($null -eq $proposal) { throw 'Missing rotation proposal.' }
    throw 'HALT: untrusted development rotation proposal cannot be promoted or replace the active allowlist.'
}
