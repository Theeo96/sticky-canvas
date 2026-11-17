param([string]$AppPath = '')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
if(-not $AppPath){$AppPath=Join-Path $taskRoot 'bin\StickyCanvas.exe'}
$taskApp=(Resolve-Path -LiteralPath $AppPath).Path
$taskRun=Join-Path $taskRoot ('.local\image-resource-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun -Force | Out-Null
$taskStamp=Get-Date -Format 'yyyyMMdd-HHmmss'
$taskReport=Join-Path $taskRoot ('evidence\image-resources-'+$taskStamp+'.json')
New-Item -ItemType Directory -Path (Split-Path $taskReport -Parent) -Force | Out-Null
Copy-Item -LiteralPath $taskApp -Destination (Join-Path $taskRun 'StickyCanvas.exe')
Copy-Item -LiteralPath ($taskApp+'.config') -Destination (Join-Path $taskRun 'CompareRunner.exe.config')
$taskSource=@'
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using StickyCanvas;
class SoakRunner {
 static string root,report; static MemoApp app; static Stopwatch life; static List<object> samples=new List<object>();static List<double> latencies=new List<double>();static int ticks,batches,cycles;static double previousCpu;static DateTime previousTime;static bool batchSinceSample;
 static void Require(bool b,string m){if(!b)throw new Exception(m);}
 [STAThread] static int Main(string[] a){root=Path.GetFullPath(a[1]);report=Path.GetFullPath(a[2]);try{Directory.CreateDirectory(root);if(a[0]=="seed"){Seed();return 0;}if(a[0]=="verify"){Verify();return 0;}Run();return 0;}catch(Exception e){File.WriteAllText(report+".error",e.ToString());if(app!=null)app.Dispatcher.Invoke(new Action(()=>app.Shutdown()));return 1;}}
 static string Serialize(object value){return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(value);}
 static string Hash(string path){using(var stream=File.OpenRead(path))return Store.Sha(stream);}
 static void Seed(){
  var assets=new List<Asset>();using(var store=new Store(root)){
   for(int i=0;i<3;i++){int width=i==2?5000:4096,height=i==2?5000:3072;string path=Path.Combine(root,"source-"+i+".jpg");
    using(var bitmap=new System.Drawing.Bitmap(width,height,System.Drawing.Imaging.PixelFormat.Format24bppRgb)){
     var data=bitmap.LockBits(new System.Drawing.Rectangle(0,0,width,height),System.Drawing.Imaging.ImageLockMode.WriteOnly,bitmap.PixelFormat);var pixels=new byte[Math.Abs(data.Stride)*height];new Random(800+i).NextBytes(pixels);Marshal.Copy(pixels,0,data.Scan0,pixels.Length);bitmap.UnlockBits(data);
     var encoder=System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders().First(c=>c.MimeType=="image/jpeg");using(var settings=new System.Drawing.Imaging.EncoderParameters(1)){settings.Param[0]=new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality,70L);bitmap.Save(path,encoder,settings);}
    }
    assets.Add(store.ImportImage(path));
   }
   for(int i=0;i<3;i++){var n=Note.Create();n.Title="Synthetic large images "+i;n.PaperWidth=1600;n.PaperHeight=1200;n.Elements.Clear();foreach(var asset in assets)n.Elements.Add(new Element{Type="image",AssetId=asset.Id,X=20+n.Elements.Count*420,Y=180,Width=320,Height=320.0*asset.PixelHeight/asset.PixelWidth});store.Save(n,new NoteView{Left=100+i*30,Top=100+i*30,Width=640,Height=540});}
   File.WriteAllText(report+".fixtures",Serialize(assets.Select(x=>new {x.PixelWidth,x.PixelHeight,x.Bytes}).ToList()));
  }
 }
 static void Snapshot(string stage){using(var p=Process.GetCurrentProcess()){p.Refresh();double cpu=p.TotalProcessorTime.TotalMilliseconds;var now=DateTime.UtcNow;samples.Add(new{Seconds=Math.Round(life.Elapsed.TotalSeconds,2),Stage=stage,WorkingMiB=Math.Round(p.WorkingSet64/1048576.0,2),PrivateMiB=Math.Round(p.PrivateMemorySize64/1048576.0,2),ManagedMiB=Math.Round(GC.GetTotalMemory(false)/1048576.0,2),IntervalCpuMs=cpu-previousCpu,IntervalSeconds=(now-previousTime).TotalSeconds,IncludesBatch=batchSinceSample,OpenWindows=app.OpenWindows.Count});previousCpu=cpu;previousTime=now;batchSinceSample=false;File.WriteAllText(report+".progress",Serialize(new{Seconds=life.Elapsed.TotalSeconds,Batches=batches,Cycles=cycles,WorkingMiB=p.WorkingSet64/1048576.0,PrivateMiB=p.PrivateMemorySize64/1048576.0,Windows=app.OpenWindows.Count}));}}
 static void Batch(){
  for(int round=0;round<5;round++)foreach(var w in app.OpenWindows.Values.ToList()){
   var watch=Stopwatch.StartNew();var image=w.Model.Elements.First(e=>e.Type=="image");string id=image.Id,asset=image.AssetId;double originalWidth=image.Width;
   w.BeginImageOpacity(image);int decodes=app.Store.BitmapDecodeCount;for(int j=0;j<100;j++)w.PreviewImageOpacity(j/100.0);Require(app.Store.BitmapDecodeCount==decodes,"Preview decoded images");w.FinishImageEdit(true);w.Undo();w.Redo();w.Undo();
   image=w.Model.Elements.First(e=>e.Id==id);w.BeginImageCrop(image);var r=w.PendingCrop;w.SetCropRectangle(new Rect(r.X+4,r.Y+4,r.Width*.85,r.Height*.85));w.FinishImageEdit(true);w.Undo();
   Require(w.Model.Elements.First(e=>e.Id==id).Width==originalWidth,"Crop undo geometry drift");Require(w.Model.Elements.First(e=>e.Id==id).AssetId==asset,"Source asset changed");Require(w.Flush(false),"Flush failure");w.ZoomAt(round%2==0?1.02:1/1.02,new Point(150,200));latencies.Add(watch.Elapsed.TotalMilliseconds);cycles++;
  }batches++;batchSinceSample=true;
 }
 static void Run(){
  app=new MemoApp(root,0);app.Startup+=delegate{app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate{
   life=Stopwatch.StartNew();previousTime=DateTime.UtcNow;using(var p=Process.GetCurrentProcess())previousCpu=p.TotalProcessorTime.TotalMilliseconds;Snapshot("ready");
   var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};timer.Tick+=delegate{try{ticks++;Snapshot("sample");
    if(ticks==2||ticks==6||ticks==10||ticks==14||ticks==18)Batch();
    if(ticks==20)foreach(var w in app.OpenWindows.Values.ToList())w.Close();
    if(ticks==24)foreach(var n in app.Notes.ToList())app.Open(n,false);
    if(ticks==28)foreach(var w in app.OpenWindows.Values.ToList())w.Close();
    if(ticks>=30){timer.Stop();Finish();}
   }catch(Exception ex){timer.Stop();File.WriteAllText(report+".error",ex.ToString());app.Shutdown();}};timer.Start();
  }));};try{app.Run();}finally{app.Dispose();}
 }
 static void Finish(){
  foreach(var w in app.OpenWindows.Values)Require(w.Flush(false),"Final flush");app.Store.Export(Path.Combine(root,"large-images-backup.zip"));var notes=app.Store.Load();
  File.WriteAllText(Path.Combine(root,"expected.json"),Json.Write(notes));Snapshot("complete");
  File.WriteAllText(report,Serialize(new{Version=System.Reflection.Assembly.GetAssembly(typeof(Note)).GetName().Version.ToString(),DurationSeconds=life.Elapsed.TotalSeconds,NoteCount=notes.Count,ImageReferences=notes.Sum(n=>n.Elements.Count(e=>e.Type=="image")),Batches=batches,EditCycles=cycles,DecodeCount=app.Store.BitmapDecodeCount,CacheBytes=app.Store.BitmapCacheBytes,PeakWorkingMiB=Process.GetCurrentProcess().PeakWorkingSet64/1048576.0,MedianCycleMs=latencies.OrderBy(x=>x).ElementAt(latencies.Count/2),MaxCycleMs=latencies.Max(),Samples=samples,NoForcedGc=true,NoProductionData=true,Status="completed"}));app.Quit();
 }
 static void Verify(){using(var store=new Store(root)){
  var notes=store.Load();Require(store.ReadErrors.Count==0,"Read errors after restart");Require(Json.Write(notes)==File.ReadAllText(Path.Combine(root,"expected.json")),"Persisted note mismatch");
  var assets=notes.SelectMany(n=>n.Elements).Select(e=>e.AssetId).Distinct().Select(store.GetAsset).ToList();foreach(var asset in assets)Require(Hash(store.AssetPath(asset))==asset.Id,"Asset checksum mismatch");
  using(var restore=new Store(Path.Combine(root,"restore-profile"))){var copies=restore.Import(Path.Combine(root,"large-images-backup.zip"));Require(copies.Count==3 && copies.Sum(n=>n.Elements.Count)==9,"Backup import mismatch");foreach(var e in copies.SelectMany(n=>n.Elements))Require(restore.GetAsset(e.AssetId)!=null,"Restored asset missing");}
  File.WriteAllText(report+".restart",Serialize(new{RestartDataMatch=true,AssetChecksumsVerified=assets.Count,RestoredNotes=3,RestoredImages=9}));}}
}

'@
$taskSourcePath=Join-Path $taskRun 'Runner.cs'
[IO.File]::WriteAllText($taskSourcePath,$taskSource,(New-Object Text.UTF8Encoding($true)))
$taskFramework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$taskCompiler='C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
if(-not(Test-Path $taskCompiler)){$taskCompiler=Join-Path $taskFramework 'csc.exe'}
$taskRefs=@('System.dll','System.Core.dll','System.Drawing.dll','System.Xaml.dll','System.Runtime.Serialization.dll','System.Web.Extensions.dll') | ForEach-Object {'/r:'+(Join-Path $taskFramework $_)}
$taskRefs+=@('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object {'/r:'+(Join-Path (Join-Path $taskFramework 'WPF') $_)}
$taskExe=Join-Path $taskRun 'CompareRunner.exe'
& $taskCompiler /nologo /target:winexe /platform:x64 /optimize+ ('/out:'+$taskExe) ('/r:'+(Join-Path $taskRun 'StickyCanvas.exe')) @taskRefs $taskSourcePath
if($LASTEXITCODE -ne 0){throw 'Resource test runner compilation failed'}
$taskProfile=Join-Path $taskRun 'profile'
foreach($taskMode in @('seed','run','verify')){
 $taskProcess=Start-Process -FilePath $taskExe -ArgumentList @($taskMode,('"'+$taskProfile+'"'),('"'+$taskReport+'"')) -WindowStyle Hidden -PassThru
 if(-not $taskProcess.WaitForExit(360000)){throw ('Test helper timed out; profile: '+$taskProfile)}
 if($taskProcess.ExitCode -ne 0 -or(Test-Path ($taskReport+'.error'))){throw ('Image resource test failed; report: '+$taskReport)}
}
[pscustomobject]@{Report=$taskReport;Profile=$taskProfile;Version=(Get-Item $taskApp).VersionInfo.FileVersion}
