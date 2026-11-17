using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace StickyCanvas {
 [DataContract] public sealed class DpiReport {
  [DataMember] public int AttachedMonitors;
  [DataMember] public List<double> AttachedScalePercent=new List<double>();
  [DataMember] public List<double> RenderedScalePercent=new List<double>();
  [DataMember] public bool GeometryRoundTrips;
  [DataMember] public string Scope="Actual monitor inventory plus offscreen render/coordinate checks; physical mixed-DPI movement not verified.";
 }
 public static class DpiChecks {
  [StructLayout(LayoutKind.Sequential)] struct Bounds {public int L,T,R,B;}
  [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref Bounds rect,uint flags);
  [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor,int type,out uint x,out uint y);
  public static DpiReport Run(){
   var report=new DpiReport();foreach(var screen in System.Windows.Forms.Screen.AllScreens){var area=screen.Bounds;var bounds=new Bounds{L=area.Left,T=area.Top,R=area.Right,B=area.Bottom};uint x,y;int result=GetDpiForMonitor(MonitorFromRect(ref bounds,2),0,out x,out y);if(result!=0)throw new InvalidOperationException("DPI inventory failed");report.AttachedMonitors++;report.AttachedScalePercent.Add(x/96.0*100);}
   foreach(double scale in new[]{1.0,1.25,1.5,1.75,2.0}){
    var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawRectangle(Brushes.Red,null,new Rect(20,20,40,40));
    var bitmap=new RenderTargetBitmap((int)Math.Ceiling(100*scale),(int)Math.Ceiling(100*scale),96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(visual);var bytes=new byte[4];bitmap.CopyPixels(new Int32Rect((int)(40*scale),(int)(40*scale),1,1),bytes,4,0);if(bytes[2]!=255||bytes[3]!=255)throw new InvalidOperationException("DPI render mismatch");
    foreach(double zoom in new[]{.1,1.0,4.0,8.0}){var doc=new Point(123.4,456.7);var transform=new Matrix(zoom*scale,0,0,zoom*scale,-75.5,38.2);var physical=transform.Transform(doc);transform.Invert();var restored=transform.Transform(physical);if((restored-doc).Length>1e-8)throw new InvalidOperationException("DPI coordinate drift");}
    report.RenderedScalePercent.Add(scale*100);
   }
   report.GeometryRoundTrips=true;return report;
  }
 }
 public sealed partial class NoteWindow {
  protected override void OnDpiChanged(DpiScale oldDpi,DpiScale newDpi){base.OnDpiChanged(oldDpi,newDpi);Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render,new Action(delegate{if(IsLoaded){ApplyZoom();RefreshPenPointer();}}));}
 }
}
