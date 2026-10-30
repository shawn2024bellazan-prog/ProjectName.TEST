$ErrorActionPreference = 'Stop'
$root = 'G:\ProjectName'
Set-Location -LiteralPath $root
function HashFile([string]$path) {
  $stream = [System.IO.File]::OpenRead($path)
  $sha = [System.Security.Cryptography.SHA256]::Create()
  try { return [System.BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
  finally { $sha.Dispose(); $stream.Dispose() }
}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$output = "evidence\builds\sha256-$stamp.csv"
$paths = & git ls-files --others --exclude-standard
if ($LASTEXITCODE -ne 0) { throw 'Git inventory failed' }
$rows = foreach ($relative in $paths) {
  if ($relative -like 'evidence/*' -or $relative -like '.git/*') { continue }
  $absolute = Join-Path $root ($relative.Replace('/', '\'))
  if (Test-Path -LiteralPath $absolute -PathType Leaf) {
    [pscustomobject]@{ Path=$relative; Sha256=(HashFile $absolute) }
  }
}
$rows | Sort-Object Path | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath $output
Write-Host "SOURCE_HASH_SNAPSHOT: $output"
Write-Host "HASHED_FILES: $(@($rows).Count)"
