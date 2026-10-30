# Fixed-clock, deterministic, mock-only negative tests for unexecuted rotation.
$ErrorActionPreference = 'Stop'
$root = 'G:\ProjectName'
. (Join-Path $root 'eng\gateway-receipt-contract.ps1')
. (Join-Path $root 'eng\coreclr-rotation-contract.ps1')
$active = Get-Content -Raw -LiteralPath (Join-Path $root 'eng\coreclr-receipts.development.json') | ConvertFrom-Json
$proposal = Get-Content -Raw -LiteralPath (Join-Path $root 'eng\coreclr-receipts.rotation-0022.proposed.json') | ConvertFrom-Json
$obs = Get-Content -Raw -LiteralPath (Join-Path $root "evidence\builds\$($proposal.replacementReceipt.name)") | ConvertFrom-Json
$activeHash = $proposal.previousManifestSha256
$obsHash = $proposal.replacementReceipt.sha256
$fixed = [datetimeoffset]::Parse('2026-10-29T23:18:00Z')
function Clone([object]$x) { return ($x | ConvertTo-Json -Depth 16 | ConvertFrom-Json) }
function Verify([object]$p,[object]$a=$active,[object]$o=$obs,[string]$ah=$activeHash,[string]$oh=$obsHash,[datetimeoffset]$clock=$fixed,[int]$head=1) {
    Assert-CoreClrRotationProposal $a $p $o $ah $oh $clock $head
}
$count = 0
Verify $proposal
$count++
Verify $proposal
$count++
function Reject([string]$label,[scriptblock]$action) {
    $rejected=$false
    try { & $action | Out-Null } catch { $rejected=$true }
    if (-not $rejected) { throw "Unsafe rotation accepted: $label" }
    $script:count++
}
$p=Clone $proposal; $p.previousRevision=0
Reject 'rollback previous revision' { Verify $p }
$p=Clone $proposal; $p.proposedRevision=1
Reject 'stale revision' { Verify $p }
$p=Clone $proposal; $p.proposedRevision=3
Reject 'revision skipped' { Verify $p }
Reject 'stale competing head' { Verify $proposal $active $obs $activeHash $obsHash $fixed 2 }
Reject 'substituted source digest' { Verify $proposal $active $obs ('A'*64) }
$p=Clone $proposal; $p.previousManifestSha256='B'*64
Reject 'proposed source tampering' { Verify $p }
$p=Clone $proposal; $p.replacementReceipt.name=$proposal.previousReceiptName
Reject 'duplicate old observation' { Verify $p }
$p=Clone $proposal; $p.replacementReceipt.sha256=$proposal.previousReceiptSha256
Reject 'duplicate old digest' { Verify $p }
Reject 'new receipt digest mismatch' { Verify $proposal $active $obs $activeHash ('C'*64) }
$p=Clone $proposal; $p.replacementReceipt.validUntilUtc='2026-10-29T23:17:00Z'
Reject 'already expired' { Verify $p }
$p=Clone $proposal; $p.replacementReceipt.validUntilUtc='2026-10-31T23:17:00Z'
Reject 'expiry beyond 24h' { Verify $p }
Reject 'fixed clock stale' { Verify $proposal $active $obs $activeHash $obsHash $fixed.AddHours(25) }
Reject 'fixed clock future' { Verify $proposal $active $obs $activeHash $obsHash $fixed.AddHours(-3) }
$p=Clone $proposal; $p.approvalRecord=[pscustomobject]@{approved=$true}
Reject 'forged approval' { Verify $p }
$p=Clone $proposal; $p | Add-Member -NotePropertyName approved -NotePropertyValue $true
Reject 'untrusted approved property' { Verify $p }
$p=Clone $proposal; $p.state='APPLIED'
Reject 'applied state' { Verify $p }
$p=Clone $proposal; $p.productionActivationAllowed=$true
Reject 'production grant claim' { Verify $p }
Reject 'no promotion route even when caller claims approval' { Assert-CoreClrRotationNotApproved $proposal }
# Missing mandatory args must error non-interactively, not guess or default.
$verifier = Join-Path $root 'eng\verify-coreclr-rotation-proposal.ps1'
$null = @(& pwsh -NoProfile -NonInteractive -File $verifier -ProposalName 'coreclr-receipts.rotation-0022.proposed.json' 2>&1)
if ($LASTEXITCODE -eq 0) { throw 'Missing mandatory observation name/hash accepted.' }
$count++
Write-Output 'CORECLR_ROTATION_CONTRACTS=PASS'
Write-Output "ROTATION_ASSERTIONS=$count"
