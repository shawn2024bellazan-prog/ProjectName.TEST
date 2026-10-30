# Synthetic child for gateway watchdog tests. No network or models.
param([ValidateSet('Pass','Fail','AbruptExit','SyntheticAccessViolation','Timeout')][string]$Mode = 'Pass')
$ErrorActionPreference = 'Stop'
switch ($Mode) {
    'Pass' { Write-Host 'SYNTHETIC_PASS'; exit 0 }
    'Fail' { Write-Host 'SYNTHETIC_FAIL_EXIT_23'; exit 23 }
    'AbruptExit' { [Environment]::Exit(-1073740791) }
    'SyntheticAccessViolation' { [Environment]::Exit(-1073741819) }
    'Timeout' { Start-Sleep -Seconds 30; exit 0 }
}
