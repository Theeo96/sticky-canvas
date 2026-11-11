using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
namespace StickyCanvas {
 public sealed partial class NoteWindow {
  sealed class PixelBuffer {public Element Element;public byte[] Bytes;public int Width,Height;public WriteableBitmap Preview;public bool Dirty;}
  Dictionary<string,PixelBuffer> eraseBuffers;
  Point? eraseLast;
  public double EraserSize {get{return View.EraserWidth>0?View.EraserWidth:20;}}
  string SavePixels(BitmapSource source) {
   string temp=System.IO.Path.Combine(App.Store.Root,"drawing-"+Guid.NewGuid().ToString("N")+".png");
   try {var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(source));using(var stream=File.Create(temp))encoder.Save(stream);return App.Store.ImportImage(temp).Id;}
   finally {if(File.Exists(temp))File.Delete(temp);}
  }
  public void RasterizeDrawing(Element e) {
   if(e.Type!="stroke")return;
   int width=Math.Max(1,(int)Math.Ceiling(e.Width)),height=Math.Max(1,(int)Math.Ceiling(e.Height));
   double scale=Math.Min(2,Math.Min(2048.0/width,2048.0/height));
   var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.PushTransform(new ScaleTransform(scale,scale));InkDrawing.ToStroke(e,e.Width,e.Height).Draw(dc);dc.Pop();}
   var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(width*scale)),Math.Max(1,(int)Math.Ceiling(height*scale)),96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();
   string id=SavePixels(bitmap);e.AssetId=id;e.Type="image";e.IsDrawing=true;e.Points=null;e.StrokeWidth=0;
  }
  public void BeginPixelErase() {if(eraseBuffers!=null)return;EndEditing();Record();eraseBuffers=new Dictionary<string,PixelBuffer>();eraseLast=null;}
  public bool ErasePixelAt(Point point) {
   if(eraseBuffers==null)BeginPixelErase();bool changed=false;
   var previous=eraseLast??point;double distance=(point-previous).Length;
   int steps=Math.Max(1,(int)Math.Ceiling(distance/Math.Max(1,EraserSize/4)));
   for(int step=1;step<=steps;step++) {
    Point p=previous+(point-previous)*(step/(double)steps);double radius=EraserSize/2;
    foreach(var e in Model.Elements.Where(e=>!e.Locked && (e.IsDrawing||e.Type=="stroke")).ToList()) {
     if(p.X+radius<e.X||p.Y+radius<e.Y||p.X-radius>e.X+e.Width||p.Y-radius>e.Y+e.Height)continue;
     PixelBuffer buffer;
     if(!eraseBuffers.TryGetValue(e.Id,out buffer)) {
      var copy=Json.Copy(e);if(copy.Type=="stroke")RasterizeDrawing(copy);
      var source=ImageSource(copy);var rgba=new FormatConvertedBitmap(source,PixelFormats.Pbgra32,null,0);
      buffer=new PixelBuffer{Element=e,Width=rgba.PixelWidth,Height=rgba.PixelHeight};buffer.Bytes=new byte[buffer.Width*buffer.Height*4];rgba.CopyPixels(buffer.Bytes,buffer.Width*4,0);buffer.Preview=new WriteableBitmap(rgba);eraseBuffers.Add(e.Id,buffer);
     }
     bool hit=false;
     int x0=Math.Max(0,(int)Math.Floor((p.X-radius-e.X)/e.Width*buffer.Width)),x1=Math.Min(buffer.Width-1,(int)Math.Ceiling((p.X+radius-e.X)/e.Width*buffer.Width));
     int y0=Math.Max(0,(int)Math.Floor((p.Y-radius-e.Y)/e.Height*buffer.Height)),y1=Math.Min(buffer.Height-1,(int)Math.Ceiling((p.Y+radius-e.Y)/e.Height*buffer.Height));
     for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++) {
      double dx=e.X+(x+.5)/buffer.Width*e.Width-p.X,dy=e.Y+(y+.5)/buffer.Height*e.Height-p.Y;int i=(y*buffer.Width+x)*4;
      if(dx*dx+dy*dy<=radius*radius && buffer.Bytes[i+3]>0){buffer.Bytes[i]=buffer.Bytes[i+1]=buffer.Bytes[i+2]=buffer.Bytes[i+3]=0;hit=true;}
     }
     if(hit){buffer.Dirty=true;changed=true;buffer.Preview.WritePixels(new Int32Rect(0,0,buffer.Width,buffer.Height),buffer.Bytes,buffer.Width*4,0);if(views.ContainsKey(e.Id))views[e.Id].ShowPixelPreview(buffer.Preview);}
    }
   }
   eraseLast=point;return changed;
  }
  public void FinishPixelErase() {
   if(eraseBuffers==null)return;var buffers=eraseBuffers;eraseBuffers=null;eraseLast=null;
   buffers=buffers.Where(p=>p.Value.Dirty).ToDictionary(p=>p.Key,p=>p.Value);if(buffers.Count==0)return;
   try {
    var assets=buffers.Values.ToDictionary(b=>b.Element.Id,b=>SavePixels(b.Preview));
    foreach(var b in buffers.Values){b.Element.AssetId=assets[b.Element.Id];b.Element.Type="image";b.Element.IsDrawing=true;b.Element.Points=null;b.Element.StrokeWidth=0;b.Element.CropX=b.Element.CropY=b.Element.CropWidth=b.Element.CropHeight=null;}
    Model.SchemaVersion=Math.Max(4,Model.SchemaVersion);Render();Record();MarkDirty();
   } catch(Exception ex){MemoApp.Error(ex);}
  }
  void BuildBrushSliders(WrapPanel toolbar) {
   Action<string,double,double,Action<double>,Func<double>> add=delegate(string label,double max,double initial,Action<double> apply,Func<double> read) {
    var panel=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(4,2,4,2),VerticalAlignment=VerticalAlignment.Center};
    panel.Children.Add(new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center});
    var slider=new Slider{Minimum=1,Maximum=max,Value=initial,Width=85,SmallChange=1,LargeChange=5,IsSnapToTickEnabled=true,TickFrequency=1,ToolTip=label+" 크기"};
    System.Windows.Automation.AutomationProperties.SetName(slider,label+" 크기");panel.Children.Add(slider);
    var preview=new Canvas{Width=36,Height=36,ClipToBounds=true,Background=Brushes.White};var dot=new Ellipse{Fill=Brushes.Black};preview.Children.Add(dot);panel.Children.Add(preview);
    var value=new TextBlock{Width=42,VerticalAlignment=VerticalAlignment.Center};panel.Children.Add(value);
    Action refresh=delegate{double n=read();double d=Math.Min(32,n);dot.Width=dot.Height=d;Canvas.SetLeft(dot,(36-d)/2);Canvas.SetTop(dot,(36-d)/2);value.Text=n.ToString("0")+" px";preview.ToolTip="실제 지름 "+n+" px (미리보기 최대 32px)";};
    slider.ValueChanged+=delegate{apply(slider.Value);refresh();MarkDirty(false);};refresh();toolbar.Children.Add(panel);
   };
   add("✎",64,View.PenWidth>0?View.PenWidth:3,n=>{View.PenWidth=n;ConfigureInkAttributes();},()=>View.PenWidth>0?View.PenWidth:3);
   add("▰",128,EraserSize,n=>View.EraserWidth=n,()=>EraserSize);
  }
 }
}
