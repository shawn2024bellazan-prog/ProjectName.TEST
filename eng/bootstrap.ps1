# Generated source bootstrap; no production registration, grants or activation.
$ErrorActionPreference = 'Stop'
$root = 'G:\ProjectName'
$seed = 'G:\workspace\marketplaces\projectname-test-0023\candidates\maf-native'
Set-Location -LiteralPath $root
function RunDotnet([string[]]$A) { & dotnet @A; if ($LASTEXITCODE -ne 0) { throw ("dotnet " + ($A -join ' ') + " failed: $LASTEXITCODE") } }
if (-not (Test-Path 'ProjectName.slnx')) { RunDotnet @('new','sln','-n','ProjectName','-f','slnx','-o',$root) }
$projects = @(
@('src\BuildingBlocks\ProjectName.Domain','classlib'),
@('src\BuildingBlocks\ProjectName.Contracts','classlib'),
@('src\BuildingBlocks\ProjectName.Application','classlib'),
@('src\BuildingBlocks\ProjectName.Infrastructure','classlib'),
@('src\BuildingBlocks\ProjectName.Persistence','classlib'),
@('src\BuildingBlocks\ProjectName.SharedKernel','classlib'),
@('src\Runtime\ProjectName.Runtime','classlib'),
@('src\Governance\ProjectName.Governance','classlib'),
@('src\Governance\ProjectName.ControlPlane','classlib'),
@('src\Governance\ProjectName.Trails','classlib'),
@('src\Governance\ProjectName.Evidence','classlib'),
@('src\Services\ProjectName.OrchestrationApi','web'),
@('src\Services\ProjectName.OrchestratorService','grpc'),
@('src\Services\ProjectName.PlannerService','grpc'),
@('src\Services\ProjectName.MakerService','grpc'),
@('src\Services\ProjectName.CheckerService','grpc'),
@('src\Services\ProjectName.ReflectorService','grpc'),
@('src\Protocols\ProjectName.Grpc.Contracts','classlib'),
@('apps\ProjectName.Cli','console'),
@('sdk\ProjectName.Sdk.DotNet','classlib'),
@('tests\ProjectName.Governance.Tests','xunit')
)
foreach ($p in $projects) {
  $path = $p[0]; $kind = $p[1]; $name = Split-Path -Leaf $path
  if (-not (Test-Path (Join-Path $path "$name.csproj"))) {
    RunDotnet @('new',$kind,'-n',$name,'-o',$path,'-f','net10.0','--no-restore')
  }
}
foreach ($name in @('ProjectName.AppHost','ProjectName.ServiceDefaults')) {
  $target = Join-Path 'src\Hosting' $name
  New-Item -Type Directory -Force -Path $target | Out-Null
  if (-not (Test-Path (Join-Path $target "$name.csproj"))) {
    Copy-Item -LiteralPath (Join-Path $seed "src\$name\$name.csproj") -Destination $target
  }
}
if (-not (Test-Path 'src\Hosting\ProjectName.AppHost\AppHost.cs')) {
  Copy-Item -LiteralPath (Join-Path $seed 'src\ProjectName.AppHost\AppHost.cs') -Destination 'src\Hosting\ProjectName.AppHost\AppHost.cs'
}
if (-not (Test-Path 'src\Hosting\ProjectName.ServiceDefaults\Extensions.cs')) {
  Copy-Item -LiteralPath (Join-Path $seed 'src\ProjectName.ServiceDefaults\Extensions.cs') -Destination 'src\Hosting\ProjectName.ServiceDefaults\Extensions.cs'
}
Get-ChildItem -Path 'src','apps','sdk','tests' -Recurse -File -Filter '*.csproj' |
 Sort-Object FullName | ForEach-Object { RunDotnet @('sln','ProjectName.slnx','add',$_.FullName,'--in-root') }
$layout = @('.agents','marketplaces\pmcro-core','marketplaces\csuite-marketplace',
 'marketplaces\coding-marketplace','marketplaces\inspection-marketplace',
 'marketplaces\agent-marketplace','templates\dotnet','mcp','docs',
 'evidence\builds','evidence\checks','.pmcro\seeds','.pmcro\policies',
 '.pmcro\registry','.pmcro\checker','apps\ProjectName.Maui',
 'apps\ProjectName.Web','sdk\typescript','sdk\python','src\Providers',
 'src\Protocols\A2A','src\Protocols\Mcp','src\Protocols\AGUI')
foreach ($relative in $layout) { New-Item -ItemType Directory -Force -Path (Join-Path $root $relative) | Out-Null }
Write-Host ("BOOTSTRAP_COMPLETE: " + (Get-ChildItem -Path $root -Recurse -Filter '*.csproj' -File | Measure-Object).Count + " projects")
