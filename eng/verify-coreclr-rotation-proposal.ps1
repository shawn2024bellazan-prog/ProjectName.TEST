# Read-only verification of an UNEXECUTED development-only rotation. No apply action.
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^coreclr-receipts\.rotation-[0-9]{4}\.proposed\.json$')][string]$ProposalName,
    [Parameter(Mandatory=$true)][ValidatePattern('^coreclr-events-\d{8}-\d{6}-\d{7}-[0-9a-f]{8}\.json$')][string]$ObservationName,
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-F0-9]{64}$')][string]$ObservationSha256
)
$ErrorActionPreference = 'Stop'
$root = 'G:\ProjectName'
. (Join-Path $root 'eng\gateway-receipt-contract.ps1')
. (Join-Path $root 'eng\coreclr-rotation-contract.ps1')
function Read-RestrictedJson([string]$path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 1 -or $bytes.Length -gt 1048576) { throw 'JSON file size rejected.' }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $digest = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','') }
    finally { $sha.Dispose() }
    return [pscustomobject]@{ Data = ([Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json); Sha256 = $digest }
}
$active = Read-RestrictedJson (Join-Path $root 'eng\coreclr-receipts.development.json')
$proposal = Read-RestrictedJson (Join-Path $root "eng\$ProposalName")
$observation = Read-RestrictedJson (Join-Path $root "evidence\builds\$ObservationName")
if ($proposal.Data.replacementReceipt.name -cne $ObservationName -or
    $proposal.Data.replacementReceipt.sha256 -cne $ObservationSha256) { throw 'Explicit proposal input mismatch.' }
Assert-CoreClrRotationProposal $active.Data $proposal.Data $observation.Data $active.Sha256 $observation.Sha256 ([datetimeoffset]::UtcNow) 1
Write-Output 'ROTATION_PROPOSAL=PASS_UNEXECUTED'
Write-Output "PROPOSED_MANIFEST_SHA256=$($proposal.Sha256)"
Write-Output 'ACTIVE_ALLOWLIST_UNCHANGED=true'
Write-Output 'PRODUCTION_ACTIVATION_ALLOWED=false'
