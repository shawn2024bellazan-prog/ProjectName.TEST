# Bounded deterministic race/approval/revocation/crash test. No I/O or trusted signing.
$ErrorActionPreference = 'Stop'
$root='G:\ProjectName'
. (Join-Path $root 'eng\gateway-receipt-contract.ps1')
. (Join-Path $root 'eng\coreclr-rotation-mock-replay.ps1')
$proposal = Get-Content -Raw -LiteralPath (Join-Path $root 'eng\coreclr-receipts.rotation-0022.proposed.json') | ConvertFrom-Json
$clock=[datetimeoffset]::Parse('2026-10-29T23:30:00Z')
$base=$proposal.previousManifestSha256
$aDigest='A'*64
$bDigest='B'*64
$approval=[pscustomobject]@{
 kind='MOCK_FIXTURE_NOT_AUTHORITY'; issuer='fixture-issuer'
 checker='fixture-independent-checker'; actor='fixture-actor'
 scope='ISOLATED_DEVELOPMENT_ONLY'; mockSeal='fixture-seal-no-cryptographic-security'
 approvalId='mock-receipt-a'; grantId='fixture-grant'; proposalSha256=$aDigest
 issuedUtc='2026-10-29T23:29:00Z'; expiresUtc='2026-10-29T23:45:00Z'
}
function Clone([object]$o){return ($o|ConvertTo-Json -Depth 16|ConvertFrom-Json)}
$counter=0
function Reject([string]$label,[scriptblock]$action) {
 $rejected=$false
 try { & $action|Out-Null } catch {$rejected=$true}
 if (-not $rejected){throw "Unsafe mock replay accepted: $label"}
 $script:counter++
}
$head=New-MockRotationHead $base 1
$a=Begin-MockRotation $head $proposal $approval $aDigest $clock @()
$counter++
$ap2=Clone $approval; $ap2.approvalId='mock-receipt-b'; $ap2.proposalSha256=$bDigest
$b=Begin-MockRotation $head $proposal $ap2 $bDigest $clock @()
$counter++
if ((Finish-MockRotation $head $a $clock @() $false) -cne 'MOCK_COMMITTED_IN_MEMORY_ONLY') {throw 'Mock A CAS rejected.'}
$counter++
Reject 'two competing proposals on same head' { Finish-MockRotation $head $b $clock @() $false }
if ($head.Revision -ne 2 -or $head.Digest -cne $aDigest){throw 'Wrong winner or head.'}
$counter++
$follow=Clone $proposal; $follow.previousRevision=2; $follow.proposedRevision=3
$follow.previousManifestSha256=$aDigest; $follow.replacementReceipt.sha256='E'*64
Reject 'replayed approval ID after commit' { Begin-MockRotation $head $follow $approval $aDigest $clock @() }
$ap3=Clone $approval; $ap3.approvalId='mock-receipt-c'; $ap3.proposalSha256='C'*64
$follow.replacementReceipt.sha256=$proposal.replacementReceipt.sha256
Reject 'reused receipt digest on new revision' { Begin-MockRotation $head $follow $ap3 ('C'*64) $clock @() }
$head2=New-MockRotationHead $base 1
$c=Begin-MockRotation $head2 $proposal $approval $aDigest $clock @()
if ((Finish-MockRotation $head2 $c $clock @() $true) -cne 'CRASH_BEFORE_PUBLICATION_NO_HEAD_CHANGE'){throw 'Crash hook failed.'}
if ($head2.Revision -ne 1 -or $head2.Digest -cne $base -or @($head2.UsedApprovalIds).Count -ne 0){throw 'Crash published partial head.'}
$counter++
if ((Finish-MockRotation $head2 $c $clock @() $false) -cne 'MOCK_COMMITTED_IN_MEMORY_ONLY'){throw 'Safe recovery retry failed.'}
$counter++
$bad=Clone $approval; $bad.issuer='spoofed-issuer'
Reject 'issuer identity spoof' { Begin-MockRotation (New-MockRotationHead $base 1) $proposal $bad $aDigest $clock @() }
$bad=Clone $approval; $bad.checker=$bad.issuer
Reject 'checker independence spoof' { Begin-MockRotation (New-MockRotationHead $base 1) $proposal $bad $aDigest $clock @() }
$bad=Clone $approval; $bad.actor='spoofed-principal'
Reject 'actor principal spoof' { Begin-MockRotation (New-MockRotationHead $base 1) $proposal $bad $aDigest $clock @() }
$bad=Clone $approval; $bad.mockSeal='signed'
Reject 'mock identity proof mismatch' { Begin-MockRotation (New-MockRotationHead $base 1) $proposal $bad $aDigest $clock @() }
$bad=Clone $approval; $bad.grantId='unknown'
Reject 'unknown effective mock grant' { Begin-MockRotation (New-MockRotationHead $base 1) $proposal $bad $aDigest $clock @() }
$bad=Clone $approval; $bad.proposalSha256=$bDigest
Reject 'approval not bound to digest' { Begin-MockRotation (New-MockRotationHead $base 1) $proposal $bad $aDigest $clock @() }
Reject 'revoked before prepare' { Begin-MockRotation (New-MockRotationHead $base 1) $proposal $approval $aDigest $clock @('fixture-grant') }
$hd=New-MockRotationHead $base 1
$token=Begin-MockRotation $hd $proposal $approval $aDigest $clock @()
Reject 'revoked after prepare before commit' { Finish-MockRotation $hd $token $clock @('fixture-grant') $false }
$counter++
Reject 'expired approval' { Begin-MockRotation (New-MockRotationHead $base 1) $proposal $approval $aDigest $clock.AddHours(1) @() }
$bad=Clone $approval; $bad.issuedUtc='2026-10-30T02:30:00Z'
Reject 'future issued evidence' { Begin-MockRotation (New-MockRotationHead $base 1) $proposal $bad $aDigest $clock @() }
$bad=Clone $proposal; $bad.previousRevision=0
Reject 'rolled back candidate revision' { Begin-MockRotation (New-MockRotationHead $base 1) $bad $approval $aDigest $clock @() }
$bad=Clone $proposal; $bad.previousManifestSha256='C'*64
Reject 'source manifest substitution' { Begin-MockRotation (New-MockRotationHead $base 1) $bad $approval $aDigest $clock @() }
$hd=New-MockRotationHead $base 1
$ticket=Begin-MockRotation $hd $proposal $approval $aDigest $clock @()
$hd.Digest='D'*64
Reject 'head altered after prepare' { Finish-MockRotation $hd $ticket $clock @() $false }
$hd=New-MockRotationHead $base 1
$ticket=Begin-MockRotation $hd $proposal $approval $aDigest $clock @()
$hd.UsedReceiptDigests=@($proposal.replacementReceipt.sha256)
Reject 'duplicate evidence injected before finish' { Finish-MockRotation $hd $ticket $clock @() $false }
Reject 'cannot cross production boundary' { Assert-NoHostPromotionRoute (New-MockRotationHead $base 1) }
Write-Output 'MOCK_ROTATION_REPLAY=PASS'
Write-Output "MOCK_REPLAY_ASSERTIONS=$counter"
Write-Output 'CRYPTOGRAPHIC_AUTHORITY_TESTED=false'
Write-Output 'REAL_PUBLICATION_PERFORMED=false'
