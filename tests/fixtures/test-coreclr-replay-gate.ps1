# Pure fail-closed mock output and AppHost/runtime isolation tests. No child processes.
$ErrorActionPreference='Stop'
. 'G:\ProjectName\eng\coreclr-replay-gate.ps1'
$valid=@('MOCK_ROTATION_REPLAY=PASS','MOCK_REPLAY_ASSERTIONS=25','CRYPTOGRAPHIC_AUTHORITY_TESTED=false','REAL_PUBLICATION_PERFORMED=false')
$count=0
Assert-MockReplayGate $valid 0; $count++
function Reject([string]$label,[scriptblock]$call) {
    $halt=$false
    try {& $call|Out-Null} catch {$halt=$true}
    if(-not $halt){throw "Unsafe replay gate accepted $label"}
    $script:count++
}
Reject 'missing' {Assert-MockReplayGate @() 0}
Reject 'malformed' {Assert-MockReplayGate @('MOCK_ROTATION_REPLAY=PASS','bad','CRYPTOGRAPHIC_AUTHORITY_TESTED=false','REAL_PUBLICATION_PERFORMED=false') 0}
Reject 'failed exit' {Assert-MockReplayGate $valid 2}
Reject 'duplicate pass' {Assert-MockReplayGate (@($valid)+@('MOCK_ROTATION_REPLAY=PASS')) 0}
Reject 'wrong assertion count' {Assert-MockReplayGate @('MOCK_ROTATION_REPLAY=PASS','MOCK_REPLAY_ASSERTIONS=24','CRYPTOGRAPHIC_AUTHORITY_TESTED=false','REAL_PUBLICATION_PERFORMED=false') 0}
Reject 'cryptographic authority claim' {Assert-MockReplayGate @('MOCK_ROTATION_REPLAY=PASS','MOCK_REPLAY_ASSERTIONS=25','CRYPTOGRAPHIC_AUTHORITY_TESTED=true','REAL_PUBLICATION_PERFORMED=false') 0}
Reject 'real publication claim' {Assert-MockReplayGate @('MOCK_ROTATION_REPLAY=PASS','MOCK_REPLAY_ASSERTIONS=25','CRYPTOGRAPHIC_AUTHORITY_TESTED=false','REAL_PUBLICATION_PERFORMED=true') 0}
Assert-MockReplaySourceIsolation @('namespace Test;','namespace Gateway;','namespace Api;');$count++
Reject 'mock script mounted in AppHost' {Assert-MockReplaySourceIsolation @('coreclr-rotation-mock-replay.ps1','namespace Gateway;','namespace Api;')}
Reject 'mock approval identity in runtime' {Assert-MockReplaySourceIsolation @('namespace Test;','MOCK_FIXTURE_NOT_AUTHORITY','namespace Api;')}
Write-Output 'REPLAY_GATE_CONTRACTS=PASS'
Write-Output "REPLAY_GATE_ASSERTIONS=$count"
