<#
.SYNOPSIS
  agent-creator generator. Turns subjects into DRAFT_UNREGISTERED, deny-by-default,
  self-inspecting agents, each with an agent file, a spec and an A2A v1.0 Agent Card.
  It never registers, grants, activates or executes anything, and never deletes files.
#>
[CmdletBinding()]
param(
  [string]$MarketplaceRoot,
  [string]$SubjectsFile,
  [string]$CSuiteCatalog,
  [switch]$IncludeCSuiteSeats
)
Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'
$assets = Join-Path $PSScriptRoot '..\assets'
if (-not $MarketplaceRoot) { $MarketplaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path }
if (-not $SubjectsFile) { $SubjectsFile = Join-Path $MarketplaceRoot 'subjects.json' }
if (-not $CSuiteCatalog) { $CSuiteCatalog = Join-Path $MarketplaceRoot '..\csuite-marketplace\marketplace.json' }
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Write-Text([string]$Path, [string]$Text) {
  $dir = Split-Path $Path -Parent
  if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
  $clean = ($Text -replace "`r`n", "`n").TrimEnd() + "`n"
  [System.IO.File]::WriteAllText($Path, $clean, $utf8)
}
function Read-Json([string]$p) { Get-Content -LiteralPath $p -Raw -Encoding UTF8 | ConvertFrom-Json }
function Get-Opt($o, [string]$n, $d) { if ($o.PSObject.Properties[$n]) { $o.$n } else { $d } }
function To-Bullets($items) { (@($items) | ForEach-Object { '- ' + $_ }) -join "`n" }

$Kinds = @{
  'repo' = @{
    questions = @('Which commit or release did I read, and what is its identifier?', 'What does this repository provide, according to its own files?', 'Which of its conventions should ProjectName reuse instead of reinventing?', 'Which of my statements about it are still unverified?')
    evidence = @('recorded commit or release identifier', 'file paths read', 'findings stated with file references')
    acceptance = @('every finding cites a file path at a recorded commit', 'no claim about behavior that was not read or run')
    constraint = 'I make no claim about this repository until I have recorded the commit I read.'
  }
  'local-repo' = @{
    questions = @('What is the current commit and is the tree clean?', 'What builds and which tests pass, as shown by real output?', 'Which docs claim more than the code implements?', 'What is the one smallest next increment?')
    evidence = @('git HEAD digest', 'real build and test output', 'SHA-256 of changed files')
    acceptance = @('implemented behavior is separated from placeholders', 'every PASS names the command that produced it')
    constraint = 'I read and propose only; I never alter the subject or its historical evidence.'
  }
  'object' = @{
    questions = @('What do I observe, according to what the owner supplied?', 'What would count as fixed?', 'What is the smallest first step the owner can reverse?', 'What can I not know from what I was given?')
    evidence = @('the photo or description the owner supplied')
    acceptance = @('steps are ordered, reversible and checkable by the owner', 'nothing is inferred beyond what was supplied')
    constraint = 'I advise only from what the owner supplied and I never act on the object.'
  }
  'process' = @{
    questions = @('What is the intent and who owns it?', 'What are the steps as they actually happen?', 'Where does the process lose or leak value?', 'What is the smallest change that can be measured?')
    evidence = @('the recorded steps or trail', 'measurements taken before a change')
    acceptance = @('each proposed change has a before and after measure', 'no step is assumed that was not observed')
    constraint = 'I recommend a change only when I can name the measure that will show it worked.'
  }
  'tool' = @{
    questions = @('What does the tool do according to its own documentation and help output?', 'Which of its operations have side effects?', 'What is the smallest safe use for ProjectName?', 'Which operations need a human approval each time?')
    evidence = @('version string', 'documentation or help output read')
    acceptance = @('side-effecting operations are listed separately from read-only ones', 'no grant is implied by this inspection')
    constraint = 'I list what the tool can do but I never use it; using it needs an exact allowlisted grant.'
  }
  'skill' = @{
    questions = @('Does every required folder and file exist and match its template?', 'Does each reject gate refuse invalid input?', 'Which output did a validator check, and which was only read?', 'What is the smallest improvement that keeps the template intact?')
    evidence = @('validator output', 'file hashes')
    acceptance = @('SKILL.md, references, assets and scripts are all present', 'the validator exit code is recorded')
    constraint = 'I inspect myself only through the validator and I never mark my own work PASS.'
  }
  'seat' = @{
    questions = @('What is my phase of the Chief''s work and what is its input?', 'What does the previous phase hand me?', 'What would the next seat need from me?', 'What must I leave to the independent Checker?')
    evidence = @('the intent I was handed', 'the previous seat output')
    acceptance = @('my output builds only on the previous message', 'I make no decision; I recommend')
    constraint = 'I am recommendation-only and my phase output is never self-approved.'
  }
}
$ModeText = @{
  'guide' = 'I guide: I name the next smallest step and what would show it worked.'
  'fix'   = 'I propose fixes: I draft reversible changes for human review and I never apply them.'
  'both'  = 'I guide and I propose fixes. I name the next smallest step and what would show it worked. I draft reversible changes for human review and I never apply them.'
}

$subjects = @((Read-Json $SubjectsFile).subjects)
if ($IncludeCSuiteSeats) {
  $cs = Read-Json $CSuiteCatalog
  $phases = [ordered]@{ Planner = 'plan'; Maker = 'make'; Checker = 'check' }
  foreach ($c in $cs.agents) {
    foreach ($ph in $phases.Keys) {
      $subjects += [pscustomobject]@{
        id = ('{0}-{1}-seat' -f $c.role.ToLower(), $ph.ToLower())
        kind = 'seat'; group = 'seats'; mode = 'guide'
        name = ('{0} {1} Seat' -f $c.role, $ph)
        ref = ('csuite-marketplace:{0}' -f $c.role)
        purpose = ('carry the {0} phase of {1} work for the {2}' -f $phases[$ph], $c.domain, $c.name)
      }
    }
  }
}

$seen = @{}
$catalog = @()
foreach ($s in $subjects) {
  if ($s.id -notmatch '^[a-z0-9]+(-[a-z0-9]+)*$') { throw "REJECT: invalid id '$($s.id)'" }
  if ($seen.ContainsKey($s.id)) { throw "REJECT: duplicate id '$($s.id)'" }
  $seen[$s.id] = $true
  if (-not $Kinds.ContainsKey($s.kind)) { throw "REJECT: unknown kind '$($s.kind)' for $($s.id)" }
  $mode = Get-Opt $s 'mode' 'both'
  if (-not $ModeText.ContainsKey($mode)) { throw "REJECT: unknown mode '$mode' for $($s.id)" }
  foreach ($req in 'name', 'ref', 'purpose', 'group') { if (-not (Get-Opt $s $req '')) { throw "REJECT: $($s.id) missing $req" } }
  $k = $Kinds[$s.kind]
  $agentName = if ($s.kind -eq 'seat') { $s.name } else { $s.name + ' Agent' }
  $desc = ('Draft agent that inspects {0} and {1}. Recommendation only; no tools, no grants.' -f $s.ref, $(if ($mode -eq 'guide') { 'guides' } elseif ($mode -eq 'fix') { 'proposes fixes' } else { 'guides and proposes fixes' }))
  $seed = ('@{0} /agent-creator:inspect "Inspect {1} under the PMCR-O cycle; record evidence and the earned constraint; request an independent Checker; do not claim PASS, register, grant or execute."' -f $s.id, $s.ref)

  $spec = Get-Content (Join-Path $assets 'agent-spec.template.json') -Raw | ConvertFrom-Json
  $spec.id = $s.id; $spec.name = $agentName; $spec.group = $s.group; $spec.mode = $mode
  $spec.subject.kind = $s.kind; $spec.subject.ref = $s.ref; $spec.subject.purpose = $s.purpose
  $spec.identity.statement = ('I am the {0}. My only interface is the intent I am given; I run ORCHESTRATE, PLAN, MAKE, CHECK and REFLECT on my own subject and stop at my authority boundary.' -f $agentName)
  $spec.selfInspection.questions = @($k.questions)
  $spec.selfInspection.evidence = @($k.evidence)
  $spec.selfInspection.acceptance = @($k.acceptance)
  $spec.earnedConstraint = $k.constraint
  $spec.nextSeed = $seed

  $card = Get-Content (Join-Path $assets 'agent-card.template.json') -Raw | ConvertFrom-Json
  $card.name = $agentName; $card.description = $desc
  $card.supportedInterfaces[0].url = ('https://agents.invalid/{0}/a2a' -f $s.id)
  $skills = @()
  $skills += [pscustomobject]@{ id = "$($s.id).inspect"; name = 'Self-inspect'; description = 'Inspect the subject through PMCR-O self-reference and return evidence-bound findings.'; tags = @('self-inspection', $s.kind, $s.group); examples = @("Inspect $($s.ref) and report findings with evidence.") }
  if ($mode -ne 'fix') { $skills += [pscustomobject]@{ id = "$($s.id).guide"; name = 'Guide'; description = 'Name the next smallest step toward the goal. Recommendation only.'; tags = @('guide', $s.kind, $s.group); examples = @("What is the next smallest step for $($s.ref)?") } }
  if ($mode -ne 'guide') { $skills += [pscustomobject]@{ id = "$($s.id).propose-fix"; name = 'Propose fix'; description = 'Draft a reversible fix for human review. Never applies it.'; tags = @('propose-fix', $s.kind, $s.group); examples = @("Propose a reversible fix for $($s.ref).") } }
  $card.skills = $skills

  $md = (Get-Content (Join-Path $assets 'agent.md.template') -Raw -Encoding UTF8)
  $map = @{ '{{ID}}' = $s.id; '{{NAME}}' = $agentName; '{{DESCRIPTION}}' = $desc; '{{PURPOSE}}' = $s.purpose; '{{KIND}}' = $s.kind; '{{REF}}' = $s.ref; '{{MODE_TEXT}}' = $ModeText[$mode]; '{{QUESTIONS}}' = (To-Bullets $k.questions); '{{ACCEPTANCE}}' = (To-Bullets $k.acceptance); '{{CONSTRAINT}}' = $k.constraint; '{{NEXTSEED}}' = $seed }
  foreach ($key in $map.Keys) { $md = $md.Replace($key, [string]$map[$key]) }

  $dir = Join-Path $MarketplaceRoot ('agents\' + $s.id)
  Write-Text (Join-Path $dir ($s.id + '.agent.md')) $md
  Write-Text (Join-Path $dir 'agent.spec.json') ($spec | ConvertTo-Json -Depth 10)
  Write-Text (Join-Path $dir 'agent-card.json') ($card | ConvertTo-Json -Depth 10)
  $catalog += [pscustomobject]@{ id = $s.id; name = $agentName; group = $s.group; kind = $s.kind; mode = $mode; path = ('agents/' + $s.id) }
}

$mpPath = Join-Path $MarketplaceRoot 'marketplace.json'
$mp = Read-Json $mpPath
$mp.agents = @($catalog | Sort-Object group, id)
$mp.count = $catalog.Count
Write-Text $mpPath ($mp | ConvertTo-Json -Depth 10)
Write-Output ("Generated {0} draft agents (all DRAFT_UNREGISTERED, deny-by-default)." -f $catalog.Count)
