param([string]$Root = 'G:\ProjectName')
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $Root
$core = Get-Content -LiteralPath 'marketplaces\pmcro-core\marketplace.json' -Raw | ConvertFrom-Json
$candidate = Get-Content -LiteralPath 'marketplaces\pmcro-core\cascading-orchestration.candidate.json' -Raw | ConvertFrom-Json
$roles = @('orchestrator','planner','maker','checker','reflector')
$phases = @('orchestrate','plan','make','check','reflect')
if ($core.cascadingCandidate.path -cne 'cascading-orchestration.candidate.json' -or
    $core.cascadingCandidate.state -cne 'DRAFT_UNREGISTERED' -or
    $core.cascadingCandidate.productionAdmission -cne 'DENY' -or
    $core.cascadingCandidate.checkerPass) {
    throw 'MARKETPLACE_CANDIDATE_NOT_DENIED'
}
if ($candidate.status -cne 'DRAFT_UNREGISTERED' -or
    $candidate.authority -cne 'CANDIDATE_ONLY' -or
    $candidate.productionAdmission -cne 'DENY' -or
    $candidate.registration -or $candidate.grant -or $candidate.activation -or
    $candidate.workersStarted -ne 0 -or $candidate.modelsCalled -ne 0) {
    throw 'CANDIDATE_PRETENDS_TO_HAVE_AUTHORITY'
}
if (@($candidate.agents).Count -ne 5 -or
    @($candidate.skills).Count -ne 5 -or
    (@($candidate.agents) -join ',') -cne ($roles -join ',') -or
    (@($candidate.skills) -join ',') -cne ($phases -join ',')) {
    throw 'FIVE_BY_FIVE_ROLE_PHASE_MATRIX_CHANGED'
}
if ($candidate.commandGrammar -cne '@{agent} /pmcro:{skill} "{intent}"' -or
    -not $candidate.invariant.ordered -or
    -not $candidate.invariant.noSkippedPhases -or
    -not $candidate.invariant.independentCheckerRequired -or
    $candidate.invariant.checkerPass -or
    $candidate.invariant.nextSeedExecuted -or
    $candidate.selfApplication.maxDepth -ne 1 -or
    $candidate.selfApplication.weightTraining -or
    $candidate.trailTraining.autoFineTune) {
    throw 'CANDIDATE_INVARIANTS_INVALID'
}
if (@($candidate.sample).Count -ne 5) { throw 'MISSING_ROLE_SAMPLES' }
foreach ($role in $roles) {
    $matching = @($candidate.sample | Where-Object { $_.agent -ceq $role })
    if ($matching.Count -ne 1 -or
        -not $matching[0].command.StartsWith("@$role /pmcro:orchestrate ", [StringComparison]::Ordinal)) {
        throw "ROLE_COMMAND_MISSING_OR_SPOOFED_$role"
    }
}
foreach ($file in @('docs\adr\README.md',
                    'docs\adr\0001-cascading-orchestrator-authority.md',
                    'docs\adr\0002-structured-commands-and-trail-training.md')) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "MISSING_ADR_$file" }
}
Write-Host 'CASCADING_CONTRACT=PASS'
Write-Host 'SELF_APPLICATION_ROLE_PHASE_CELLS=25'
Write-Host 'SIMULATED_ONLY=true'
Write-Host 'INDEPENDENT_CHECKER_PASS=false'
Write-Host 'PRODUCTION_ADMISSION=DENY'
