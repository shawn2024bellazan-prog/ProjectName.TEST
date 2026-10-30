# Mechanical conversion of our newly generated projects to central package versions.
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath 'G:\ProjectName'
Get-ChildItem -Path 'src','apps','sdk','tests' -Filter '*.csproj' -Recurse -File |
  Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.Name -ne 'ProjectName.Maui.csproj' } |
  ForEach-Object {
    $raw = Get-Content -LiteralPath $_.FullName -Raw
    $next = [regex]::Replace($raw,
      '(<PackageReference\b[^>]*?)\s+Version="[^"]+"', '$1')
    if ($next -ne $raw) {
      [System.IO.File]::WriteAllText($_.FullName, $next, [System.Text.UTF8Encoding]::new($false))
      Write-Host ("CENTRALIZED " + $_.Name)
    }
  }
