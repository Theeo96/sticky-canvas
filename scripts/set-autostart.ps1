param([bool]$Enabled = $true)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskExe = [System.IO.Path]::GetFullPath((Join-Path $taskRoot 'release\current\StickyCanvas.exe'))
if (-not (Test-Path -LiteralPath $taskExe)) { throw 'Package the current release first.' }
$taskKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if ($Enabled) {
    $taskValue = '"' + $taskExe + '"'
    if ($taskValue.Length -gt 260) { throw 'Startup path exceeds the Run key command limit.' }
    if (-not (Test-Path -LiteralPath $taskKey)) { New-Item -Path $taskKey | Out-Null }
    New-ItemProperty -LiteralPath $taskKey -Name StickyCanvas -Value $taskValue -PropertyType String -Force | Out-Null
} else {
    Remove-ItemProperty -LiteralPath $taskKey -Name StickyCanvas -ErrorAction SilentlyContinue
}
Get-ItemProperty -LiteralPath $taskKey -Name StickyCanvas -ErrorAction SilentlyContinue | Select-Object StickyCanvas
