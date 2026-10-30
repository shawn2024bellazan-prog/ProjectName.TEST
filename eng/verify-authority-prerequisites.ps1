# Read-only host evidence preflight. Does not grant, create keys, or activate.
# Signed inputs, if ever provisioned by the host, live OUTSIDE the repository:
# C:\ProgramData\ProjectName\authority\{issuer,grant,revocations,checker,kms}.json
# Format: { purpose, payloadBase64, signatureBase64, certificateSha256Hex }.
# The signed payload is UTF-8 "purpose\n" + JSON evidence.
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath 'G:\ProjectName'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$receipt = "evidence\builds\authority-preflight-$stamp.json"
$hostRoot = Join-Path $env:ProgramData 'ProjectName\authority'
$stateRoot = [Environment]::GetEnvironmentVariable('PROJECTNAME_PROTECTED_CHECKPOINT_DIR')
$checkerDir = 'G:\ProjectName\.pmcro\checker'
$registryDir = 'G:\ProjectName\.pmcro\registry'
$policyDir = 'G:\ProjectName\.pmcro\policies'

function Test-PrivateDirectoryAcl([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path) -or
        -not [IO.Directory]::Exists($path)) { return $false }
    if (([IO.File]::GetAttributes($path) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        return $false
    }
    try {
        $acl = Get-Acl -LiteralPath $path
        $ownerSid = $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value
        if ($ownerSid -notin @("S-1-5-18","S-1-5-32-544")) { return $false }
        $broad = @('Everyone','BUILTIN\Users','NT AUTHORITY\Authenticated Users',
            'S-1-1-0','S-1-5-32-545','S-1-5-11')
        $writes = [int]([Security.AccessControl.FileSystemRights]::WriteData -bor
            [Security.AccessControl.FileSystemRights]::CreateFiles -bor
            [Security.AccessControl.FileSystemRights]::CreateDirectories -bor
            [Security.AccessControl.FileSystemRights]::Delete -bor
            [Security.AccessControl.FileSystemRights]::ChangePermissions -bor
            [Security.AccessControl.FileSystemRights]::TakeOwnership)
        foreach ($rule in $acl.Access) {
            if ($rule.AccessControlType -ne 'Allow') { continue }
            if ($broad -contains $rule.IdentityReference.Value -and
                (([int]$rule.FileSystemRights -band $writes) -ne 0)) { return $false }
        }
        return $true
    } catch { return $false }
}

function Read-SignedHostEvidence([string]$name, [string]$purpose) {
    if (-not (Test-PrivateDirectoryAcl $hostRoot)) { return $null }
    $path = Join-Path $hostRoot "$name.json"
    if (-not [IO.File]::Exists($path)) { return $null }
    try {
        $e = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
        if ($e.purpose -cne $purpose -or
            $e.certificateSha256Hex -notmatch '^[0-9a-fA-F]{64}$') { return $null }
        $payload = [Convert]::FromBase64String($e.payloadBase64)
        $signature = [Convert]::FromBase64String($e.signatureBase64)
        if ($payload.Length -gt 16384 -or $signature.Length -ne 64) { return $null }
        $prefix = [Text.Encoding]::UTF8.GetBytes("$purpose`n")
        if ($payload.Length -le $prefix.Length) { return $null }
        for ($i = 0; $i -lt $prefix.Length; $i++) {
            if ($payload[$i] -ne $prefix[$i]) { return $null }
        }
        $store = [Security.Cryptography.X509Certificates.X509Store]::new(
            'TrustedPeople',[Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
        try {
            $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
            foreach ($cert in $store.Certificates) {
                $certHash = [Convert]::ToHexString(
                    $cert.GetCertHash([Security.Cryptography.HashAlgorithmName]::SHA256))
                if ($certHash -cne $e.certificateSha256Hex.ToUpperInvariant() -or
                    [DateTime]::UtcNow -lt $cert.NotBefore.ToUniversalTime() -or
                    [DateTime]::UtcNow -ge $cert.NotAfter.ToUniversalTime()) { continue }
                $keyUsages = @($cert.Extensions | Where-Object {
                    $_ -is [Security.Cryptography.X509Certificates.X509KeyUsageExtension]
                })
                if ($keyUsages.Count -eq 0 -or
                    -not (($keyUsages[0].KeyUsages -band
                      [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature) -ne 0)) {
                    continue
                }
                $key = [Security.Cryptography.X509Certificates.ECDsaCertificateExtensions]::GetECDsaPublicKey($cert)
                if ($null -eq $key) { continue }
                try {
                    if (-not $key.VerifyData($payload,$signature,
                        [Security.Cryptography.HashAlgorithmName]::SHA256,
                        [Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)) {
                        continue
                    }
                    $body = [Text.Encoding]::UTF8.GetString($payload, $prefix.Length,
                        $payload.Length - $prefix.Length) | ConvertFrom-Json
                    return [pscustomobject]@{ Body = $body; CertificateSha256 = $certHash }
                } finally { $key.Dispose() }
            }
        } finally { $store.Close() }
    } catch { return $null }
    return $null
}

$identityObserved = $false
$sidHash = $null
try {
    $windows = [Security.Principal.WindowsIdentity]::GetCurrent()
    $identityObserved = $windows.IsAuthenticated -and ($null -ne $windows.User)
    if ($identityObserved) {
        $sidBytes = [Text.Encoding]::UTF8.GetBytes($windows.User.Value)
        $sidHash = [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData($sidBytes))
    }
} catch { $identityObserved = $false }

$sourceFile = 'G:\ProjectName\src\BuildingBlocks\ProjectName.Persistence\PostgresTransactionalCheckpointCandidate.cs'
$sourceDigest = if ([IO.File]::Exists($sourceFile)) {
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [IO.File]::ReadAllBytes($sourceFile)))
} else { $null }
$issuer = Read-SignedHostEvidence 'issuer' 'pmcro.issuer.assertion.v1'
$grant = Read-SignedHostEvidence 'grant' 'pmcro.grant.v1'
$revocation = Read-SignedHostEvidence 'revocations' 'pmcro.revocation.v1'
$checker = Read-SignedHostEvidence 'checker' 'pmcro.checker.receipt.v1'
$kms = Read-SignedHostEvidence 'kms' 'pmcro.kms.configuration.v1'
$now = [DateTimeOffset]::UtcNow

$issuerVerified = $identityObserved -and $null -ne $issuer -and
    $issuer.Body.actorSidSha256 -ceq $sidHash -and
    $issuer.Body.artifactSha256 -ceq $sourceDigest -and
    [DateTimeOffset]::Parse($issuer.Body.expiresAtUtc) -gt $now
$grantVerified = $issuerVerified -and $null -ne $grant -and
    $grant.CertificateSha256 -ceq $issuer.CertificateSha256 -and
    $grant.Body.actorSidSha256 -ceq $sidHash -and
    $grant.Body.artifactSha256 -ceq $sourceDigest -and
    [DateTimeOffset]::Parse($grant.Body.expiresAtUtc) -gt $now -and
    -not [string]::IsNullOrWhiteSpace($grant.Body.grantId)
$revocationVerified = $grantVerified -and $null -ne $revocation -and
    $revocation.CertificateSha256 -ceq $issuer.CertificateSha256 -and
    [DateTimeOffset]::Parse($revocation.Body.expiresAtUtc) -gt $now -and
    @($revocation.Body.revokedGrantIds) -cnotcontains $grant.Body.grantId
$checkerVerified = $issuerVerified -and $null -ne $checker -and
    $checker.CertificateSha256 -cne $issuer.CertificateSha256 -and
    $checker.Body.artifactSha256 -ceq $sourceDigest -and
    $checker.Body.checkerId -cne $issuer.Body.actorId -and
    -not [string]::IsNullOrWhiteSpace($checker.Body.evidenceReference) -and
    [DateTimeOffset]::Parse($checker.Body.expiresAtUtc) -gt $now
$kmsConfigurationVerified = $null -ne $kms -and
    $kms.CertificateSha256 -ceq $issuer.CertificateSha256 -and
    -not [string]::IsNullOrWhiteSpace($kms.Body.providerId) -and
    -not [string]::IsNullOrWhiteSpace($kms.Body.keyReference) -and
    ($null -eq $kms.Body.rawKeyBytes)
$aclVerified = Test-PrivateDirectoryAcl $stateRoot
$gateway = Get-Content -Raw -LiteralPath 'G:\ProjectName\src\Services\ProjectName.AgentGateway\Program.cs'
$api = Get-Content -Raw -LiteralPath 'G:\ProjectName\src\Services\ProjectName.OrchestrationApi\Program.cs'
$result = [ordered]@{
    scope = 'G:\ProjectName development authority discovery only'
    hostWindowsIdentityObserved = [bool]$identityObserved
    hostIdentityIssuerVerified = [bool]$issuerVerified
    signedCheckerRouteVerified = [bool]$checkerVerified
    effectiveGrantRegistryVerified = [bool]$grantVerified
    revocationEvidenceVerified = [bool]$revocationVerified
    protectedKmsConfigurationVerified = [bool]$kmsConfigurationVerified
    protectedKeySourceVerified = $false # Signed config is not a live KMS attestation.
    privateStorageAclVerified = [bool]$aclVerified
    checkerArtifactCount = @(Get-ChildItem -LiteralPath $checkerDir -Recurse -File).Count
    registryArtifactCount = @(Get-ChildItem -LiteralPath $registryDir -Recurse -File).Count
    policyArtifactCount = @(Get-ChildItem -LiteralPath $policyDir -Recurse -File).Count
    gatewayExplicitDenyAll = $gateway.Contains('DenyAllVerifiedIdentityResolver') -and
        $gateway.Contains('DenyAllAuthorityRepository') -and $gateway.Contains('DenyAllReceiptVerifier')
    orchestrationApiExplicitDenyAll = $api.Contains('DenyAllAuthorityRepository') -and
        $api.Contains('DenyAllReceiptVerifier')
    independentCheckerPass = $false # Preflight never creates a Checker verdict.
    productionActivationAllowed = $false
    sourceSha256 = $sourceDigest
    verdict = 'HALT'
    reason = 'PRODUCTION_AUTHORITY_AND_INDEPENDENT_CHECKER_NOT_VERIFIED'
    timestampUtc = $now.ToString('O')
}
$result | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 -LiteralPath $receipt
Write-Host "AUTHORITY_PREFLIGHT=$($result.verdict)"
Write-Host "IDENTITY_OBSERVED=$identityObserved"
Write-Host "ISSUER_VERIFIED=$issuerVerified"
Write-Host "CHECKER_VERIFIED=$checkerVerified"
Write-Host "GRANT_VERIFIED=$grantVerified"
Write-Host "REVOCATION_VERIFIED=$revocationVerified"
Write-Host "KMS_CONFIGURATION_VERIFIED=$kmsConfigurationVerified"
Write-Host "ACL_VERIFIED=$aclVerified"
Write-Host "RECEIPT=$receipt"
