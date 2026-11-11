param([switch]$Test)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path $taskRoot 'bin'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskCompiler = 'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { $taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe' }
$taskFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$taskReferences = @('System.dll','System.Core.dll','System.Runtime.Serialization.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xaml.dll') | ForEach-Object { '/r:' + (Join-Path $taskFramework $_) }
$taskReferences += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/r:' + (Join-Path (Join-Path $taskFramework 'WPF') $_) }
$taskSources = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'apps\desktop') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$taskExe = Join-Path $taskOutput 'StickyCanvas.exe'
& (Join-Path $PSScriptRoot 'build-icon.ps1')
$taskIcon = Join-Path $taskRoot 'assets\app-icon.ico'
& $taskCompiler /nologo /target:winexe /platform:x64 /optimize+ /warnaserror+ /utf8output ('/out:' + $taskExe) ('/win32icon:' + $taskIcon) ('/resource:' + $taskIcon + ',StickyCanvas.AppIcon.ico') ('/win32manifest:' + (Join-Path $taskRoot 'apps\desktop\app.manifest')) @taskReferences @taskSources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'apps\desktop\StickyCanvas.exe.config') -Destination ($taskExe + '.config') -Force
Write-Output ('Built: ' + $taskExe)
if ($Test) {
    $taskResult = Join-Path $taskRoot 'evidence\self-test.json'
    New-Item -ItemType Directory -Path (Split-Path $taskResult -Parent) -Force | Out-Null
    '{"Status":"running"}' | Set-Content -LiteralPath $taskResult -Encoding utf8
    $taskProcess = Start-Process -FilePath $taskExe -ArgumentList @('--self-test', ('"' + $taskResult + '"')) -WindowStyle Hidden -PassThru
    if (-not $taskProcess.WaitForExit(30000)) { Stop-Process -Id $taskProcess.Id -Force; throw 'Self-test timed out after 30 seconds.' }
    if ($taskProcess.ExitCode -ne 0) { throw ('Self-test failed; see ' + $taskResult) }
    Get-Content -LiteralPath $taskResult
}
