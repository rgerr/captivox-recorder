param([Parameter(Mandatory = $true)][string]$AppDir)

# Sluit vóór het verwijderen de Captivox die uit deze programmamap draait, eerst netjes en anders na 8 seconden hard.
# Alleen processen waarvan het programma in deze map staat; een andere Captivox op dezelfde pc blijft ongemoeid.
$ErrorActionPreference = 'SilentlyContinue'
$prefix = $AppDir.TrimEnd('\') + '\'
Get-CimInstance Win32_Process |
    Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object {
        $process = Get-Process -Id $_.ProcessId
        if ($process) {
            [void]$process.CloseMainWindow()
            if (-not $process.WaitForExit(8000)) {
                Stop-Process -Id $process.Id -Force
            }
        }
    }
exit 0
