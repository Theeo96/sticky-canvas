using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
namespace StickyCanvas {
 public sealed partial class NoteWindow {
  Grid imageEditRoot;
  Border imageEditBar;
  Canvas cropLayer;
  Element imageTarget,imageOriginal;
  Rect cropBounds,fullImage;
  double pendingOpacity;
  bool cropMode;
  public bool ImageEditing {get{return imageTarget!=null;}}
  public Rect PendingCrop {get{return cropBounds;}}
  void InitializeImageEditing(Grid root) {
   imageEditRoot=root;SizeChanged+=delegate{if(imageEditBar!=null)imageEditBar.MaxWidth=Math.Max(200,ActualWidth-20);};
   PreviewMouseDown+=delegate(object sender,MouseButtonEventArgs e) {
    if(!ImageEditing)return;
    var source=e.OriginalSource as DependencyObject;
    if(IsInside(imageEditBar,source)||IsInside(cropLayer,source))return;
    if(e.ChangedButton==MouseButton.Middle)return;
    if(cropMode){if(e.ChangedButton==MouseButton.Left && e.ClickCount==2)FinishImageEdit(true);e.Handled=true;}
    else FinishImageEdit(true);
   };
   PreviewKeyDown+=delegate(object sender,KeyEventArgs e) {
    if(!ImageEditing)return;
    if(e.Key==Key.Escape){FinishImageEdit(false);e.Handled=true;}
    else if(e.Key==Key.Enter){FinishImageEdit(true);e.Handled=true;}
    else if((Keyboard.Modifiers&ModifierKeys.Control)!=0 && e.Key==Key.Z){FinishImageEdit(false);e.Handled=true;}
    else if((Keyboard.Modifiers&ModifierKeys.Control)!=0 && e.Key==Key.S)FinishImageEdit(true);
    else if(cropMode && (e.Key==Key.Delete||e.Key==Key.Back))e.Handled=true;
   };
  }
  static bool IsInside(DependencyObject parent,DependencyObject child) {
   if(parent==null)return false;
   while(child!=null){if(child==parent)return true;child=child is Visual?VisualTreeHelper.GetParent(child):LogicalTreeHelper.GetParent(child);}return false;
  }
  bool StartImageEdit(Element e,bool crop) {
   if(e==null||e.Type!="image"||e.Locked)return false;
   CloseNoteOpacity();FinishImageEdit(true);SetInkTool("select");EndEditing();
   if(views.ContainsKey(e.Id)){Select(views[e.Id]);RevealSelection();}
   imageOriginal=Json.Copy(e);imageTarget=e;cropMode=crop;pendingOpacity=e.ImageOpacity??1;
   selectedIds.Clear();Selected=null;RefreshMultiSelection();
   saveTimer.Stop();return true;
  }
  void ShowImageBar(WrapPanel panel) {
   panel.Children.Add(UI.Button("✓ 적용",()=>FinishImageEdit(true),"Enter 또는 이미지 밖 더블클릭으로 자르기 확정"));
   panel.Children.Add(UI.Button("× 취소",()=>FinishImageEdit(false),"Esc: 이번 편집 취소"));
   imageEditBar=new Border{MaxWidth=Math.Max(200,ActualWidth-20),Child=panel,Background=UI.Brush(Model.Background),BorderBrush=ChromeForeground,BorderThickness=new Thickness(1),Padding=new Thickness(5),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(4)};
   ThemeTree(imageEditBar,UI.Brush(Model.Background));imageEditRoot.Children.Add(imageEditBar);
  }
  public void BeginImageOpacity(Element e) {
   if(!StartImageEdit(e,false))return;
   var panel=new WrapPanel{Orientation=Orientation.Horizontal};
   panel.Children.Add(new TextBlock{Text="◐ 불투명도",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4)});
   var value=new TextBlock{Text=(pendingOpacity*100).ToString("0")+"%",Width=42,VerticalAlignment=VerticalAlignment.Center};
   var slider=new Slider{Minimum=0,Maximum=100,Value=pendingOpacity*100,Width=150,TickFrequency=1,IsSnapToTickEnabled=true,SmallChange=1,LargeChange=10,Margin=new Thickness(6),ToolTip="0%=완전 투명, 100%=원본"};
   System.Windows.Automation.AutomationProperties.SetName(slider,"이미지 불투명도");
   slider.ValueChanged+=delegate{PreviewImageOpacity(slider.Value/100);value.Text=slider.Value.ToString("0")+"%";};panel.Children.Add(slider);panel.Children.Add(value);ShowImageBar(panel);
  }
  public void PreviewImageOpacity(double opacity) {
   if(imageTarget==null||cropMode)return;pendingOpacity=Math.Max(0,Math.Min(1,opacity));
   if(views.ContainsKey(imageTarget.Id))views[imageTarget.Id].PreviewOpacity(pendingOpacity);
  }
  public void BeginImageCrop(Element e) {
   if(e==null||e.Type!="image"||e.Locked)return;
   var source=App.Store.Bitmap(e.AssetId,e.IsDrawing?8192:1600);
   if(!StartImageEdit(e,true))return;
   double cx=e.CropX??0,cy=e.CropY??0,cw=e.CropWidth??1,ch=e.CropHeight??1;
   double ratio=(source.PixelWidth*cw)/(source.PixelHeight*ch);
   double w=e.IsDrawing?e.Width:Math.Min(e.Width,e.Height*ratio),h=e.IsDrawing?e.Height:w/ratio;
   double x=e.X+(e.Width-w)/2,y=e.Y+(e.Height-h)/2;
   fullImage=new Rect(x-cx*w/cw,y-cy*h/ch,w/cw,h/ch);cropBounds=new Rect(cx*fullImage.Width,cy*fullImage.Height,w,h);
   cropLayer=new Canvas{Width=fullImage.Width,Height=fullImage.Height,Background=Brushes.Transparent};Canvas.SetLeft(cropLayer,fullImage.X);Canvas.SetTop(cropLayer,fullImage.Y);Paper.Children.Add(cropLayer);
   cropLayer.Children.Add(new Image{Source=source,Width=fullImage.Width,Height=fullImage.Height,Opacity=pendingOpacity,Stretch=Stretch.Fill,IsHitTestVisible=false});
   if(views.ContainsKey(e.Id))views[e.Id].Visibility=Visibility.Hidden;
   BuildCropFrame();
   var panel=new WrapPanel{Orientation=Orientation.Horizontal};panel.Children.Add(new TextBlock{Text="⌗ 자르기 · 바깥 더블클릭 확정",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4)});ShowImageBar(panel);
  }
  Grid cropFrame;
  Rectangle[] cropShades;
  void BuildCropFrame() {
   cropShades=new Rectangle[4];for(int i=0;i<4;i++){cropShades[i]=new Rectangle{Fill=UI.Brush("#99000000"),IsHitTestVisible=false};cropLayer.Children.Add(cropShades[i]);}
   cropFrame=new Grid{Background=Brushes.Transparent};cropLayer.Children.Add(cropFrame);
   cropFrame.Children.Add(new Border{BorderBrush=Brushes.White,BorderThickness=new Thickness(2),IsHitTestVisible=false});
   for(int i=1;i<3;i++) {double n=i/3.0;var vertical=new Line{Stroke=Brushes.White,StrokeThickness=1,IsHitTestVisible=false};var horizontal=new Line{Stroke=Brushes.White,StrokeThickness=1,IsHitTestVisible=false};cropFrame.Children.Add(vertical);cropFrame.Children.Add(horizontal);cropFrame.SizeChanged+=delegate{vertical.X1=vertical.X2=cropFrame.ActualWidth*n;vertical.Y2=cropFrame.ActualHeight;horizontal.Y1=horizontal.Y2=cropFrame.ActualHeight*n;horizontal.X2=cropFrame.ActualWidth;};}
   for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++){if(x==0&&y==0)continue;int sx=x,sy=y;Point start=new Point();Rect before=new Rect();
    var handle=new Thumb{Width=x==0?24:12,Height=y==0?24:12,Background=Brushes.Black,BorderBrush=Brushes.White,BorderThickness=new Thickness(2),HorizontalAlignment=x<0?HorizontalAlignment.Left:x>0?HorizontalAlignment.Right:HorizontalAlignment.Center,VerticalAlignment=y<0?VerticalAlignment.Top:y>0?VerticalAlignment.Bottom:VerticalAlignment.Center,Cursor=x==0?Cursors.SizeNS:y==0?Cursors.SizeWE:x==y?Cursors.SizeNWSE:Cursors.SizeNESW,ToolTip="자르기 경계 조절"};
    handle.DragStarted+=delegate{start=Mouse.GetPosition(cropLayer);before=cropBounds;};
    handle.DragDelta+=delegate{var d=Mouse.GetPosition(cropLayer)-start;double l=before.Left,t=before.Top,r=before.Right,b=before.Bottom;double minW=Math.Max(16,fullImage.Width*.001),minH=Math.Max(16,fullImage.Height*.001);if(sx<0)l=Math.Min(r-minW,l+d.X);if(sx>0)r=Math.Max(l+minW,r+d.X);if(sy<0)t=Math.Min(b-minH,t+d.Y);if(sy>0)b=Math.Max(t+minH,b+d.Y);SetCropRectangle(new Rect(l,t,Math.Max(minW,r-l),Math.Max(minH,b-t)));};cropFrame.Children.Add(handle);
   }
   RefreshCropFrame();
  }
  public void SetCropRectangle(Rect rectangle) {
   if(imageTarget==null||!cropMode)return;
   double left=Math.Max(0,-fullImage.X),top=Math.Max(0,-fullImage.Y),right=Math.Min(fullImage.Width,Model.PaperWidth-fullImage.X),bottom=Math.Min(fullImage.Height,Model.PaperHeight-fullImage.Y);
   double w=Math.Max(Math.Max(16,fullImage.Width*.001),Math.Min(right-left,rectangle.Width)),h=Math.Max(Math.Max(16,fullImage.Height*.001),Math.Min(bottom-top,rectangle.Height));
   cropBounds=new Rect(Math.Max(left,Math.Min(right-w,rectangle.X)),Math.Max(top,Math.Min(bottom-h,rectangle.Y)),w,h);RefreshCropFrame();
  }
  void RefreshCropFrame() {
   if(cropFrame==null)return;Canvas.SetLeft(cropFrame,cropBounds.X);Canvas.SetTop(cropFrame,cropBounds.Y);cropFrame.Width=cropBounds.Width;cropFrame.Height=cropBounds.Height;
   var regions=new[]{new Rect(0,0,fullImage.Width,cropBounds.Top),new Rect(0,cropBounds.Bottom,fullImage.Width,Math.Max(0,fullImage.Height-cropBounds.Bottom)),new Rect(0,cropBounds.Top,cropBounds.Left,cropBounds.Height),new Rect(cropBounds.Right,cropBounds.Top,Math.Max(0,fullImage.Width-cropBounds.Right),cropBounds.Height)};
   for(int i=0;i<4;i++){var r=regions[i];Canvas.SetLeft(cropShades[i],r.X);Canvas.SetTop(cropShades[i],r.Y);cropShades[i].Width=r.Width;cropShades[i].Height=r.Height;}
  }
  public void FinishImageEdit(bool commit) {
   if(imageTarget==null)return;
   var target=imageTarget;var candidate=Json.Copy(imageOriginal);bool crop=cropMode;imageTarget=null;
   if(crop) {candidate.CropX=cropBounds.X/fullImage.Width;candidate.CropY=cropBounds.Y/fullImage.Height;candidate.CropWidth=cropBounds.Width/fullImage.Width;candidate.CropHeight=cropBounds.Height/fullImage.Height;candidate.X=fullImage.X+cropBounds.X;candidate.Y=fullImage.Y+cropBounds.Y;candidate.Width=cropBounds.Width;candidate.Height=cropBounds.Height;}
   else candidate.ImageOpacity=pendingOpacity;
   if(imageEditBar!=null)imageEditRoot.Children.Remove(imageEditBar);if(cropLayer!=null)Paper.Children.Remove(cropLayer);imageEditBar=null;cropLayer=null;cropFrame=null;cropShades=null;
   if(views.ContainsKey(target.Id)){views[target.Id].Visibility=Visibility.Visible;views[target.Id].PreviewOpacity(imageOriginal.ImageOpacity??1);}
   if(dirty)saveTimer.Start();
   if(!commit || target.Locked || !Model.Elements.Contains(target))return;
   var test=Json.Copy(Model);test.SchemaVersion=Math.Max(3,test.SchemaVersion);test.Elements[test.Elements.FindIndex(e=>e.Id==target.Id)]=candidate;Rules.Validate(test);
   if(Json.Write(candidate)==Json.Write(imageOriginal))return;
   if(!crop){Record();target.ImageOpacity=pendingOpacity;Model.SchemaVersion=Math.Max(3,Model.SchemaVersion);if(views.ContainsKey(target.Id))views[target.Id].PreviewOpacity(pendingOpacity);Render();Record();MarkDirty();return;}
   Edit(delegate{target.CropX=candidate.CropX;target.CropY=candidate.CropY;target.CropWidth=candidate.CropWidth;target.CropHeight=candidate.CropHeight;target.X=candidate.X;target.Y=candidate.Y;target.Width=candidate.Width;target.Height=candidate.Height;target.ImageOpacity=candidate.ImageOpacity;Model.SchemaVersion=Math.Max(3,Model.SchemaVersion);});
  }
 }
}
