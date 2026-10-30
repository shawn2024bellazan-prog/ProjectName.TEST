$ErrorActionPreference = 'Stop'
$root = 'G:\ProjectName'
Set-Location -LiteralPath $root
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$transcript = Join-Path $root "evidence\builds\validation-$stamp.txt"
Start-Transcript -LiteralPath $transcript | Out-Null
try {
    Write-Host 'VALIDATION: SDK'
    & dotnet --version
    if ($LASTEXITCODE -ne 0) { throw 'SDK check failed' }
    Write-Host 'VALIDATION: RESTORE'
    & dotnet restore ProjectName.slnx
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
    Write-Host 'VALIDATION: RELEASE BUILD'
    & dotnet build ProjectName.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    Write-Host 'VALIDATION: TEST'
    & dotnet test ProjectName.slnx -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    Write-Host 'VALIDATION: CLI STATUS'
    & dotnet run --project apps\ProjectName.Cli --no-build -c Release -- status
    if ($LASTEXITCODE -ne 0) { throw 'CLI status failed' }
    Write-Host 'VALIDATION: SOURCE HASHES'
    Get-ChildItem -LiteralPath $root -Recurse -File |
      Where-Object { $_.FullName -notmatch '\\(bin|obj|evidence)\\' -and
        $_.Extension -in '.cs','.csproj','.proto','.json','.props','.slnx','.ps1','.md','.yml' } |
      Sort-Object FullName | Get-FileHash -Algorithm SHA256 |
      Select-Object Path,Hash |
      Export-Csv -NoTypeInformation -Encoding utf8 -LiteralPath (Join-Path $root "evidence\builds\hashes-$stamp.csv")
    Write-Host 'VALIDATION_PASS (local build/tests only; NOT independent checker)'
} finally {
    Stop-Transcript | Out-Null
    Write-Host "Transcript: $transcript"
}
