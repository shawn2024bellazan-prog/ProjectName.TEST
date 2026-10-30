$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath 'G:\ProjectName'
$index = Get-Content '.agents\marketplace.json' -Raw | ConvertFrom-Json
$csuite = Get-Content 'marketplaces\csuite-marketplace\marketplace.json' -Raw | ConvertFrom-Json
$core = Get-Content 'marketplaces\pmcro-core\marketplace.json' -Raw | ConvertFrom-Json
if ($csuite.agents.Count -ne 10) { throw 'C-SUITE_ROLE_COUNT_MISMATCH' }
if (@($csuite.agents | Select-Object -ExpandProperty role -Unique).Count -ne 10) { throw 'DUPLICATE_CSUITE_ROLE' }
if (@($csuite.agents | Where-Object { $_.canOrchestrate }).Count -ne 2) { throw 'ORCHESTRATOR_SCOPE_MISMATCH' }
if ($csuite.activation -or $core.activation -or $index.activated.Count -ne 0) { throw 'UNAUTHORIZED_ACTIVATION' }
if ($core.roles.Count -ne 5) { throw 'PMCR_ROLE_COUNT_MISMATCH' }
& "$PSScriptRoot\verify-cascading-contract.ps1"
Write-Host 'CATALOGS_PASS: 10 draft executives, 5 PMCR-O roles, zero activations'
