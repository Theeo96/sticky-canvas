$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskFolder=Join-Path $taskRoot ('.local\dpi-check-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskFolder -Force | Out-Null
$taskReport=Join-Path $taskRoot ('evidence\dpi-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.json')
New-Item -ItemType Directory -Path (Split-Path $taskReport -Parent) -Force | Out-Null
$taskSource='using System;using System.IO;using StickyCanvas;class Probe{[STAThread]static void Main(string[] args){File.WriteAllText(args[0],Json.Write(DpiChecks.Run()));}}'
$taskSourcePath=Join-Path $taskFolder 'Probe.cs';[IO.File]::WriteAllText($taskSourcePath,$taskSource)
$taskFramework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$taskCompiler='C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
if(-not(Test-Path $taskCompiler)){$taskCompiler=Join-Path $taskFramework 'csc.exe'}
Copy-Item (Join-Path $taskRoot 'bin\StickyCanvas.exe') (Join-Path $taskFolder 'StickyCanvas.exe')
$taskRefs=@('System.dll','System.Runtime.Serialization.dll')|ForEach-Object {'/r:'+(Join-Path $taskFramework $_)}
$taskRefs+=@('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll')|ForEach-Object {'/r:'+(Join-Path (Join-Path $taskFramework 'WPF') $_)}
$taskExe=Join-Path $taskFolder 'Probe.exe'
& $taskCompiler /nologo /target:exe /platform:x64 ('/out:'+$taskExe) ('/r:'+(Join-Path $taskFolder 'StickyCanvas.exe')) ('/win32manifest:'+(Join-Path $taskRoot 'apps\desktop\app.manifest')) @taskRefs $taskSourcePath
if($LASTEXITCODE -ne 0){throw 'DPI probe compilation failed'}
& $taskExe $taskReport
if($LASTEXITCODE -ne 0){throw 'DPI checks failed'}
Get-Content $taskReport
