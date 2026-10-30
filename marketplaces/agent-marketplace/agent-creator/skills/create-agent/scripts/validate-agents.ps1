<#
.SYNOPSIS
  Validates every generated agent: files present, draft state, deny-by-default authority,
  A2A v1.0 required card fields, catalog consistency. Self-validation is NOT an independent Checker PASS.
#>
[CmdletBinding()]
param(
  [string]$MarketplaceRoot,
  [string]$EvidencePath
)
Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'
if (-not $MarketplaceRoot) { $MarketplaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path }
$failures = New-Object System.Collections.Generic.List[string]
function Fail([string]$m) { $failures.Add($m) }
function Has($o, [string]$n) { [bool]($o -and $o.PSObject.Properties[$n]) }
function Read-Json([string]$p) { Get-Content -LiteralPath $p -Raw -Encoding UTF8 | ConvertFrom-Json }

$mp = Read-Json (Join-Path $MarketplaceRoot 'marketplace.json')
if ($mp.state -ne 'DRAFT_UNREGISTERED') { Fail 'marketplace state is not DRAFT_UNREGISTERED' }
foreach ($f in 'registration', 'grant', 'activation', 'executionAllowed') { if ($mp.$f -ne $false) { Fail "marketplace flag $f is not false" } }

$agentsDir = Join-Path $MarketplaceRoot 'agents'
$dirs = @(Get-ChildItem -LiteralPath $agentsDir -Directory)
$ids = @{}
$cardNames = @{}
foreach ($d in $dirs) {
  $id = $d.Name
  $specPath = Join-Path $d.FullName 'agent.spec.json'
  $cardPath = Join-Path $d.FullName 'agent-card.json'
  $mdPath = Join-Path $d.FullName ($id + '.agent.md')
  foreach ($p in $specPath, $cardPath, $mdPath) { if (-not (Test-Path -LiteralPath $p)) { Fail "$id missing $(Split-Path $p -Leaf)" } }
  if (-not ((Test-Path $specPath) -and (Test-Path $cardPath) -and (Test-Path $mdPath))) { continue }
  $spec = Read-Json $specPath
  $card = Read-Json $cardPath
  $md = Get-Content -LiteralPath $mdPath -Raw -Encoding UTF8
  if ($ids.ContainsKey($spec.id)) { Fail "duplicate id $($spec.id)" }
  $ids[$spec.id] = $true
  if ($spec.id -ne $id) { Fail "$id folder and spec id differ" }
  if ($spec.state -ne 'DRAFT_UNREGISTERED') { Fail "$id state is not DRAFT_UNREGISTERED" }
  foreach ($f in 'registration', 'grant', 'activation', 'execution') { if ($spec.authority.$f -ne $false) { Fail "$id authority.$f is not false" } }
  if ($spec.authority.defaultAdmission -ne 'DENY') { Fail "$id defaultAdmission is not DENY" }
  if (@($spec.authority.tools).Count -ne 0) { Fail "$id holds tools" }
  if (@($spec.authority.mcpGrants).Count -ne 0) { Fail "$id holds MCP grants" }
  if ($spec.checker.verdict -ne 'NOT_CLAIMED') { Fail "$id claims a Checker verdict" }
  if ($spec.checker.independentPrincipalRequired -ne $true) { Fail "$id does not require an independent Checker" }
  if ($spec.a2a.mounted -ne $false) { Fail "$id A2A is mounted" }
  if ($spec.nextSeedExecuted -ne $false) { Fail "$id next seed executed" }
  if (-not $spec.earnedConstraint) { Fail "$id has no earned constraint" }
  if (-not $spec.nextSeed) { Fail "$id has no next seed" }
  foreach ($f in 'name', 'description', 'version', 'supportedInterfaces', 'capabilities', 'defaultInputModes', 'defaultOutputModes', 'skills') { if (-not (Has $card $f)) { Fail "$id card missing $f" } }
  if ((Has $card 'supportedInterfaces') -and @($card.supportedInterfaces).Count -ge 1) {
    foreach ($f in 'url', 'protocolBinding', 'protocolVersion') { if (-not (Has $card.supportedInterfaces[0] $f) -or -not $card.supportedInterfaces[0].$f) { Fail "$id interface missing $f" } }
    if ($card.supportedInterfaces[0].url -notlike 'https://agents.invalid/*') { Fail "$id interface url is not the .invalid placeholder" }
  } else { Fail "$id card has no interface" }
  if ((Has $card 'skills') -and @($card.skills).Count -ge 1) {
    foreach ($sk in @($card.skills)) { foreach ($f in 'id', 'name', 'description', 'tags') { if (-not (Has $sk $f) -or -not $sk.$f) { Fail "$id skill missing $f" } } }
  } else { Fail "$id card has no skills" }
  if ($cardNames.ContainsKey($card.name)) { Fail "duplicate card name $($card.name)" }
  $cardNames[$card.name] = $true
  if (-not $md.StartsWith('---')) { Fail "$id agent file lacks frontmatter" }
  if ($md -notmatch 'I am the ') { Fail "$id agent file is not first person" }
  if ($md -notmatch 'state: DRAFT_UNREGISTERED') { Fail "$id agent file lacks draft state" }
  if ($md -match '\{\{') { Fail "$id agent file has unfilled template tokens" }
}
$catIds = @($mp.agents | ForEach-Object { $_.id })
if ($mp.count -ne $dirs.Count) { Fail "catalog count $($mp.count) differs from $($dirs.Count) agent folders" }
foreach ($i in $catIds) { if (-not $ids.ContainsKey($i)) { Fail "catalog lists $i without a folder" } }
foreach ($i in $ids.Keys) { if ($catIds -notcontains $i) { Fail "folder $i missing from catalog" } }

Write-Output ("agents: {0}; failures: {1}" -f $dirs.Count, $failures.Count)
$failures | ForEach-Object { Write-Output ("FAIL: " + $_) }

if ($EvidencePath) {
  $repo = (Resolve-Path (Join-Path $MarketplaceRoot '..\..')).Path
  $files = @(Get-ChildItem -LiteralPath $MarketplaceRoot -Recurse -File | Sort-Object FullName)
  $entries = @($files | ForEach-Object { [pscustomobject]@{ path = $_.FullName.Substring($repo.Length + 1).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLower() } })
  $sha = [System.Security.Cryptography.SHA256]::Create()
  $joined = ($entries | ForEach-Object { $_.path + ':' + $_.sha256 }) -join "`n"
  $manifest = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($joined))) -replace '-', '').ToLower()
  $ev = [pscustomobject]@{
    schema = 'projectname.evidence/v1'; kind = 'agent-marketplace-self-validation'
    utc = (Get-Date).ToUniversalTime().ToString('o')
    agentCount = $dirs.Count; failureCount = $failures.Count
    result = $(if ($failures.Count -eq 0) { 'SELF_VALIDATION_PASS' } else { 'SELF_VALIDATION_FAIL' })
    checkerPass = $false; productionActivationAllowed = $false
    note = 'Self-validation is not an independent authenticated Checker PASS.'
    manifestSha256 = $manifest; files = $entries
  }
  [System.IO.File]::WriteAllText($EvidencePath, (($ev | ConvertTo-Json -Depth 6) + "`n"), (New-Object System.Text.UTF8Encoding($false)))
  Write-Output ("evidence: {0} manifestSha256={1}" -f $EvidencePath, $manifest)
}
if ($failures.Count -gt 0) { exit 1 }
