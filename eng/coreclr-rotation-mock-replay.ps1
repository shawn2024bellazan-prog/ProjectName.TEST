# Deterministic in-memory test fixture only. NEVER accept this as a signature, grant or activation.
function New-MockRotationHead([string]$digest,[int]$revision) {
    if ($digest -cnotmatch '^[A-F0-9]{64}$' -or $revision -lt 1) { throw 'Invalid mock head.' }
    return [pscustomobject]@{
        Revision = $revision; Digest = $digest; UsedReceiptDigests = @(); UsedApprovalIds = @()
        Scope = 'ISOLATED_DEVELOPMENT_ONLY'; ActiveProduction = $false
    }
}
function Assert-MockApproval([object]$approval,[string]$proposalDigest,[datetimeoffset]$clock,[string[]]$revoked) {
    if ($null -eq $approval) { throw 'Missing mock approval.' }
    Assert-RequiredFields $approval @('kind','issuer','checker','actor','scope','mockSeal',
        'approvalId','grantId','proposalSha256','issuedUtc','expiresUtc')
    if ($approval.kind -cne 'MOCK_FIXTURE_NOT_AUTHORITY' -or
        $approval.issuer -cne 'fixture-issuer' -or
        $approval.checker -cne 'fixture-independent-checker' -or
        $approval.actor -cne 'fixture-actor' -or
        $approval.scope -cne 'ISOLATED_DEVELOPMENT_ONLY' -or
        $approval.mockSeal -cne 'fixture-seal-no-cryptographic-security' -or
        $approval.grantId -cne 'fixture-grant' -or
        $approval.approvalId -cnotmatch '^mock-receipt-[a-z0-9-]{1,32}$' -or
        $proposalDigest -cnotmatch '^[A-F0-9]{64}$' -or
        $approval.proposalSha256 -cne $proposalDigest -or
        $approval.grantId -cin $revoked -or
        (Convert-StrictUtc $approval.issuedUtc) -gt $clock.AddMinutes(2) -or
        (Convert-StrictUtc $approval.expiresUtc) -le $clock -or
        (Convert-StrictUtc $approval.expiresUtc) -gt $clock.AddMinutes(30)) {
        throw 'Mock-only fixture issuer/checker/grant/expiry denied.'
    }
}
function Begin-MockRotation(
    [object]$head,[object]$candidate,[object]$approval,
    [string]$proposalDigest,[datetimeoffset]$clock,[string[]]$revoked
) {
    if ($head.Scope -cne 'ISOLATED_DEVELOPMENT_ONLY' -or $head.ActiveProduction -ne $false -or
        $candidate.state -cne 'PROPOSED_UNEXECUTED' -or
        $candidate.productionActivationAllowed -ne $false -or
        $candidate.checkerPass -ne $false -or
        $candidate.previousRevision -ne $head.Revision -or
        $candidate.proposedRevision -ne ($head.Revision + 1) -or
        $candidate.previousManifestSha256 -cne $head.Digest) {
        throw 'Mock prepare requires exact revision/digest head and DENY scope.'
    }
    Assert-MockApproval $approval $proposalDigest $clock $revoked
    if (@($head.UsedReceiptDigests) -ccontains $candidate.replacementReceipt.sha256 -or
        @($head.UsedApprovalIds) -ccontains $approval.approvalId) {
        throw 'Mock duplicate evidence or replayed approval rejected.'
    }
    return [pscustomobject]@{
        BaseRevision = [int]$head.Revision
        BaseDigest = [string]$head.Digest
        NextDigest = $proposalDigest
        NextRevision = [int]$candidate.proposedRevision
        NewReceiptDigest = [string]$candidate.replacementReceipt.sha256
        ApprovalId = [string]$approval.approvalId
        Approval = $approval
    }
}
function Finish-MockRotation(
    [object]$head,[object]$ticket,[datetimeoffset]$clock,[string[]]$revoked,
    [bool]$simulateCrashBeforePublish
) {
    if ($head.Scope -cne 'ISOLATED_DEVELOPMENT_ONLY' -or $head.ActiveProduction -ne $false -or
        $head.Revision -ne $ticket.BaseRevision -or $head.Digest -cne $ticket.BaseDigest -or
        $ticket.NextRevision -ne ($head.Revision + 1)) {
        throw 'Mock compare-and-swap head changed.'
    }
    Assert-MockApproval $ticket.Approval $ticket.NextDigest $clock $revoked
    if (@($head.UsedReceiptDigests) -ccontains $ticket.NewReceiptDigest -or
        @($head.UsedApprovalIds) -ccontains $ticket.ApprovalId) {
        throw 'Mock duplicate/replayed evidence rejected at publication.'
    }
    if ($simulateCrashBeforePublish) {
        # No mutation; orphaned prepare token cannot commit itself.
        return 'CRASH_BEFORE_PUBLICATION_NO_HEAD_CHANGE'
    }
    $head.Revision = $ticket.NextRevision
    $head.Digest = $ticket.NextDigest
    $head.UsedReceiptDigests = @($head.UsedReceiptDigests) + $ticket.NewReceiptDigest
    $head.UsedApprovalIds = @($head.UsedApprovalIds) + $ticket.ApprovalId
    return 'MOCK_COMMITTED_IN_MEMORY_ONLY'
}
function Assert-NoHostPromotionRoute([object]$head) {
    throw 'HALT: test-only fixture can never authorize a real allowlist replacement.'
}
