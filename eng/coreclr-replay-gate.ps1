# Pure development verification boundary. Never treats a mock fixture as real approval.
function Assert-MockReplayGate([string[]]$lines,[int]$exitCode) {
    if ($exitCode -ne 0 -or $null -eq $lines -or @($lines).Count -ne 4) {
        throw 'Mock replay gate denied absent or failed test output.'
    }
    $expected = @(
        'MOCK_ROTATION_REPLAY=PASS',
        'MOCK_REPLAY_ASSERTIONS=25',
        'CRYPTOGRAPHIC_AUTHORITY_TESTED=false',
        'REAL_PUBLICATION_PERFORMED=false'
    )
    $normalized = @($lines | ForEach-Object { [string]$_.Trim() })
    for ($i=0; $i -lt $expected.Count; $i++) {
        if ($normalized[$i] -cne $expected[$i]) {
            throw 'Mock replay gate denied malformed or untrusted output.'
        }
    }
}
function Assert-MockReplaySourceIsolation([string[]]$runtimeSources) {
    if ($null -eq $runtimeSources -or @($runtimeSources).Count -ne 3) {
        throw 'Three runtime source texts are required for isolation check.'
    }
    foreach ($source in $runtimeSources) {
        if ([string]::IsNullOrWhiteSpace($source) -or
            $source -match '(?i)coreclr-rotation-mock-replay|test-coreclr-rotation-replay|MOCK_FIXTURE_NOT_AUTHORITY|fixture-seal-no-cryptographic-security') {
            throw 'Mock fixture or identity unexpectedly referenced in runtime source.'
        }
    }
}
