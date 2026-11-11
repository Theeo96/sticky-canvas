param([int]$Count = 5, [switch]$IncludeHidden)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskProfile = Join-Path $taskRoot ('.local\benchmark-' + $Count + '-' + [guid]::NewGuid().ToString('N'))
$taskExe = Join-Path $taskRoot 'bin\StickyCanvas.exe'
$taskLife = if ($IncludeHidden) { 60 } else { 32 }
$taskArgs = @('--profile', ('"' + $taskProfile + '"'), '--fixture', $Count, '--lifetime', $taskLife)
if ($IncludeHidden) { $taskArgs += @('--hide-after', 29) }
$taskStartup = [Diagnostics.Stopwatch]::StartNew()
$taskProcess = Start-Process -FilePath $taskExe -ArgumentList $taskArgs -WindowStyle Hidden -PassThru
$taskReady = Join-Path $taskProfile 'benchmark-ready.txt'
while (-not (Test-Path -LiteralPath $taskReady)) {
    Start-Sleep -Milliseconds 100
    if ($taskProcess.HasExited) { throw 'App exited during startup.' }
    if ($taskStartup.Elapsed.TotalSeconds -gt 15) { throw 'App startup timed out.' }
}
$taskStartup.Stop()
Start-Sleep -Seconds 3
$taskResults = @()
function Measure-TaskPhase([string]$Phase) {
    $taskBefore = Get-Process -Id $taskProcess.Id
    $taskCpuBefore = $taskBefore.TotalProcessorTime.TotalMilliseconds
    $taskWatch = [Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Seconds 20
    $taskAfter = Get-Process -Id $taskProcess.Id
    $taskWatch.Stop()
    [pscustomobject]@{
        Phase=$Phase
        Notes=$Count
        WorkingSetMiB=[math]::Round($taskAfter.WorkingSet64/1MB,2)
        PrivateBytesMiB=[math]::Round($taskAfter.PrivateMemorySize64/1MB,2)
        CpuMilliseconds=[math]::Round($taskAfter.TotalProcessorTime.TotalMilliseconds-$taskCpuBefore,3)
        IntervalSeconds=[math]::Round($taskWatch.Elapsed.TotalSeconds,3)
        AverageCpuPercent=[math]::Round(($taskAfter.TotalProcessorTime.TotalMilliseconds-$taskCpuBefore)/($taskWatch.Elapsed.TotalMilliseconds*[Environment]::ProcessorCount)*100,5)
        LogicalProcessors=[Environment]::ProcessorCount
        ProcessCount=1
    }
}
$taskResults += Measure-TaskPhase 'windows-open-idle'
if ($IncludeHidden) {
    $taskHidden = Join-Path $taskProfile 'benchmark-hidden.txt'
    while (-not (Test-Path -LiteralPath $taskHidden)) { Start-Sleep -Milliseconds 200 }
    if ((Get-Content -LiteralPath $taskHidden) -ne '0') { throw 'A note window remained open.' }
    Start-Sleep -Seconds 2
    $taskResults += Measure-TaskPhase 'all-windows-closed-tray-resident'
}
$taskProcess.WaitForExit(15000) | Out-Null
if (-not $taskProcess.HasExited) { throw 'Fixture app did not exit through the normal Quit path.' }
$taskReport = [pscustomobject]@{
    Version=(Get-Item -LiteralPath $taskExe).VersionInfo.FileVersion
    TimestampUtc=[DateTime]::UtcNow.ToString('o')
    Environment=[Environment]::OSVersion.VersionString
    StartupReadyMilliseconds=$taskStartup.ElapsedMilliseconds
    Fixture='Each note: Korean text box and vector ellipse; no large image; no UI automation attached.'
    NoForcedGcOrWorkingSetTrimming=$true
    NormalQuitConfirmed=$true
    Samples=$taskResults
}
$taskEvidence = Join-Path $taskRoot ('evidence\resources-' + $Count + '.json')
$taskReport | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $taskEvidence -Encoding utf8
$taskReport | ConvertTo-Json -Depth 6
