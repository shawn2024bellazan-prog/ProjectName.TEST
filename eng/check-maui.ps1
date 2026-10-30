$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath 'G:\ProjectName'
Write-Host 'SDK:'
dotnet --version
Write-Host 'Workloads:'
dotnet workload list
Write-Host 'MAUI project targets: net10.0; workload must match this SDK.'
