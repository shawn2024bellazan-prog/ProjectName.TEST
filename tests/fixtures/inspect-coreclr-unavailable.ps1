# Synthetic error injection only; never manufactures a successful event receipt.
$ErrorActionPreference = 'Stop'
function Get-WinEvent { throw 'SimulatedEventLogUnavailable' }
try {
    & 'G:\ProjectName\eng\inspect-coreclr-events.ps1' -StartTime ([datetime]'2026-10-29T15:00:00')
    throw 'UnexpectedSuccess'
} catch {
    if ($_.Exception.Message -like '*SimulatedEventLogUnavailable*') {
        Write-Output 'UNAVAILABLE_EVENT_LOG_REJECTED=PASS'
        exit 0
    }
    throw
}
