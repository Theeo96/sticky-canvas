using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace StickyCanvas {
 public sealed partial class NoteWindow {
  Button flowButton, zoomButton;
  readonly HashSet<string> selectedIds = new HashSet<string>();
  Dictionary<string, Point> dragPositions;
  Point? panStart, boxStart;
  double panH, panV;
  Rectangle selectionBox;
  public int SelectionCount { get { return selectedIds.Count; } }
  public void SelectMany(ElementView v, bool additive) {
   EndEditing();
   if (!additive && v != null && selectedIds.Contains(v.Model.Id) && selectedIds.Count > 1) { Selected = v; return; }
   if (!additive) selectedIds.Clear();
   if (v != null) {
    var ids = Model.Elements.Where(e => e.Id == v.Model.Id || (v.Model.GroupId != null && e.GroupId == v.Model.GroupId)).Select(e => e.Id).ToList();
    bool remove = additive && ids.All(id => selectedIds.Contains(id));
    foreach (var id in ids) { if (remove) selectedIds.Remove(id); else selectedIds.Add(id); }
   }
   Selected = v != null && selectedIds.Contains(v.Model.Id) ? v : null;
   RefreshMultiSelection(); UpdateSelectionInfo();
  }
  void RefreshMultiSelection() {
   selectedIds.RemoveWhere(id => !Model.Elements.Any(e => e.Id == id));
   if(selectionFrame!=null){Paper.Children.Remove(selectionFrame);selectionFrame=null;}
   foreach (var v in views.Values) v.SetSelected(selectedIds.Contains(v.Model.Id) && ControlsVisible);
   UpdateSelectionFrame();
  }
  public void SelectAllElements() { EndEditing(); selectedIds.Clear(); foreach(var e in Model.Elements) selectedIds.Add(e.Id); Selected = views.Values.FirstOrDefault(); RefreshMultiSelection(); }
  public void SetGroup(bool group) {
   if (SelectedLocked)return;
   if (selectedIds.Count == 0 || (group && selectedIds.Count < 2)) return;
   string id = group ? Guid.NewGuid().ToString("N") : null;
   Edit(delegate { Model.SchemaVersion = Math.Max(3,Model.SchemaVersion); foreach(var e in Model.Elements.Where(e => selectedIds.Contains(e.Id))) e.GroupId = id; });
  }
  public void StartGroupDrag(Element primary) {
   if (!selectedIds.Contains(primary.Id)) { selectedIds.Clear(); selectedIds.Add(primary.Id); }
   resizeSnapshot = Model.Elements.Where(e=>selectedIds.Contains(e.Id)).Select(e=>Json.Copy(e)).ToList();
   dragPositions = Model.Elements.Where(e => selectedIds.Contains(e.Id)).ToDictionary(e => e.Id, e => new Point(e.X,e.Y));
  }
  public void MoveGroupDrag(Element primary, double dx, double dy) {
   if (SelectedLocked) return;
   if (dragPositions == null || dragPositions.Count == 0) return;
   var anchor = dragPositions[primary.Id];
   dx = (View.SnapGrid ? Math.Round((anchor.X+dx)/8)*8 : anchor.X+dx)-anchor.X;
   dy = (View.SnapGrid ? Math.Round((anchor.Y+dy)/8)*8 : anchor.Y+dy)-anchor.Y;
   var items = Model.Elements.Where(e => dragPositions.ContainsKey(e.Id)).ToList();
   dx = Math.Max(-dragPositions.Values.Min(p => p.X), dx); dy = Math.Max(-dragPositions.Values.Min(p => p.Y), dy);
   if (View.Flow) GrowTo(items.Max(e => dragPositions[e.Id].X+e.Width)+dx+32, items.Max(e => dragPositions[e.Id].Y+e.Height)+dy+32);
   dx = Math.Min(dx, items.Min(e => Model.PaperWidth-e.Width-dragPositions[e.Id].X));
   dy = Math.Min(dy, items.Min(e => Model.PaperHeight-e.Height-dragPositions[e.Id].Y));
   foreach(var e in items) { e.X=dragPositions[e.Id].X+dx; e.Y=dragPositions[e.Id].Y+dy; views[e.Id].Layout(); }
   UpdateSelectionFrame();
  }
  double Scale { get { return Paper.LayoutTransform.Value.M11; } }
  public void ExpandArea(double right, double bottom) {
   double scale=Scale; View.Fit=false; View.Zoom=scale;
   Edit(delegate { GrowTo(Model.PaperWidth+right,Model.PaperHeight+bottom); });
  }
  void GrowTo(double width,double height) {
   Model.PaperWidth=Math.Min(8192,Math.Max(Model.PaperWidth,width)); Model.PaperHeight=Math.Min(8192,Math.Max(Model.PaperHeight,height));
   Paper.Width=Model.PaperWidth; Paper.Height=Model.PaperHeight;
   if(inkLayer!=null) { inkLayer.Width=Model.PaperWidth; inkLayer.Height=Model.PaperHeight; }
  }
  public void ToggleFlow() {
   EndEditing(); SetInkTool("select");
   if (!View.Flow) { View.BasicPanX=scroll.HorizontalOffset;View.BasicPanY=scroll.VerticalOffset;View.BasicZoom=Scale; View.BasicFit=View.Fit; View.Flow=true; View.Fit=false; View.Zoom=View.FlowZoom > 0 ? View.FlowZoom : Scale; }
   else { View.FlowPanX=scroll.HorizontalOffset;View.FlowPanY=scroll.VerticalOffset;View.FlowZoom=Scale; View.Flow=false; View.Zoom=View.BasicZoom > 0 ? View.BasicZoom : 1; View.Fit=View.BasicFit; }
   flowButton.Content=View.Flow ? "Flow ●" : "Flow"; ApplyZoom();UpdateLayout();scroll.ScrollToHorizontalOffset(View.Flow ? View.FlowPanX : View.BasicPanX);scroll.ScrollToVerticalOffset(View.Flow ? View.FlowPanY : View.BasicPanY); MarkDirty(false);
  }
  public void ZoomAt(double factor, Point point) {
   double old=Scale, zoom=Math.Max(.1,Math.Min(4,old*factor));
   double x=(scroll.HorizontalOffset+point.X-200)/old, y=(scroll.VerticalOffset+point.Y-200)/old;
   View.Fit=false; View.Zoom=zoom; ApplyZoom(); UpdateLayout();
   scroll.ScrollToHorizontalOffset(200+x*zoom-point.X); scroll.ScrollToVerticalOffset(200+y*zoom-point.Y); MarkDirty(false);
  }
  public void RevealSelection() {
   if(Selected==null)return; UpdateLayout();
   scroll.ScrollToHorizontalOffset(200+Selected.Model.X*Scale-40);
   scroll.ScrollToVerticalOffset(200+Selected.Model.Y*Scale-(ControlsVisible ? chromePanel.ActualHeight+24 : 40)); MarkDirty(false);
  }
  void AreaMenu() {
   var m=new ContextMenu(); Action<string,Action> add=(label,action)=> {var i=new MenuItem{Header=label};i.Click+=delegate{action();};m.Items.Add(i);};
   add("오른쪽 +480",()=>ExpandArea(480,0)); add("아래쪽 +360",()=>ExpandArea(0,360));
   add("너비·높이 지정…",delegate {double w,h;if(PropertyDialog.PaperSize(this,Model.PaperWidth,Model.PaperHeight,out w,out h)){ View.Zoom=Scale;View.Fit=false;Edit(delegate{Model.PaperWidth=w;Model.PaperHeight=h;});}});
   add("전체 보기",delegate{View.Fit=true;ApplyZoom();UpdateLayout();scroll.ScrollToHorizontalOffset(200);scroll.ScrollToVerticalOffset(200);MarkDirty(false);});
   add("100% 보기",delegate{View.Fit=false;View.Zoom=1;ApplyZoom();MarkDirty(false);});
   add("선택 항목으로 이동",RevealSelection);TrackMenu(m);m.IsOpen=true;
  }
  void SelectionMenu() {
   var m=new ContextMenu(); Action<string,Action> add=(label,action)=>{var i=new MenuItem{Header=label};i.Click+=delegate{action();};m.Items.Add(i);};
   add("↔ 가로 간격 맞춤",()=>Distribute(false));add("↕ 세로 간격 맞춤",()=>Distribute(true));
   add("▣ 선택 잠금",()=>LockSelection(true));add("□ 선택 잠금 해제",()=>LockSelection(false));
   add("모두 선택",SelectAllElements);add("그룹 만들기",()=>SetGroup(true));add("그룹 해제",()=>SetGroup(false));add("선택 항목으로 이동",RevealSelection);
   add("선택 항목 삭제",delegate {if(selectedIds.Count>0 && !SelectedLocked)Edit(delegate {Model.Elements.RemoveAll(e=>selectedIds.Contains(e.Id));});}); TrackMenu(m);m.IsOpen=true;
  }
  void InitializeNavigation() {
   Loaded+=delegate {flowButton.Content=View.Flow ? "Flow ●":"Flow";scroll.ScrollToHorizontalOffset(View.PanX>0?View.PanX:200);scroll.ScrollToVerticalOffset(View.PanY>0?View.PanY:200);};
   scroll.ScrollChanged+=delegate {if(!loading) MarkDirty(false);};
   PreviewMouseWheel+=delegate(object sender,MouseWheelEventArgs e) {
    bool ctrl=(Keyboard.Modifiers&ModifierKeys.Control)!=0;
    if(ctrl || (View.Flow && ActiveText==null && !TitleEditing)) {ZoomAt(e.Delta>0?1.1:1/1.1,e.GetPosition(scroll));e.Handled=true;}
   };
   scroll.PreviewMouseDown+=delegate(object sender,MouseButtonEventArgs e) {
    if(e.ChangedButton==MouseButton.Middle || (e.ChangedButton==MouseButton.Left && Keyboard.IsKeyDown(Key.Space) && ActiveText==null && !TitleEditing)) {
     panStart=e.GetPosition(scroll);panH=scroll.HorizontalOffset;panV=scroll.VerticalOffset;scroll.CaptureMouse();e.Handled=true;
    } else if(e.ChangedButton==MouseButton.Left && e.OriginalSource==Paper && inkTool=="select") {
     EndEditing();boxStart=e.GetPosition(Paper);if((Keyboard.Modifiers&ModifierKeys.Shift)==0)selectedIds.Clear();
     selectionBox=new Rectangle{Stroke=Brushes.SteelBlue,StrokeThickness=1,Fill=UI.Brush("#224C81B8"),IsHitTestVisible=false};Paper.Children.Add(selectionBox);scroll.CaptureMouse();e.Handled=true;
    }
   };
   scroll.PreviewMouseMove+=delegate(object sender,MouseEventArgs e) {
    if(panStart.HasValue) {var d=e.GetPosition(scroll)-panStart.Value;scroll.ScrollToHorizontalOffset(panH-d.X);scroll.ScrollToVerticalOffset(panV-d.Y);e.Handled=true;}
    if(boxStart.HasValue) {var r=new Rect(boxStart.Value,e.GetPosition(Paper));Canvas.SetLeft(selectionBox,r.X);Canvas.SetTop(selectionBox,r.Y);selectionBox.Width=r.Width;selectionBox.Height=r.Height;e.Handled=true;}
    if(View.Flow && inkTool=="pen" && e.LeftButton==MouseButtonState.Pressed && !panStart.HasValue) {var p=e.GetPosition(Paper);if(p.X>Model.PaperWidth-32 || p.Y>Model.PaperHeight-32) {GrowTo(p.X+128,p.Y+128);MarkDirty();}}
   };
   scroll.LostMouseCapture+=delegate {panStart=null;if(selectionBox!=null)Paper.Children.Remove(selectionBox);boxStart=null;};
   scroll.PreviewMouseUp+=delegate(object sender,MouseButtonEventArgs e) {
    if(panStart.HasValue) {panStart=null;scroll.ReleaseMouseCapture();MarkDirty(false);e.Handled=true;}
    if(boxStart.HasValue) {var r=new Rect(boxStart.Value,e.GetPosition(Paper));Paper.Children.Remove(selectionBox);boxStart=null;scroll.ReleaseMouseCapture();
     foreach(var v in views.Values.Where(v=>r.IntersectsWith(new Rect(v.Model.X,v.Model.Y,v.Model.Width,v.Model.Height)))) { selectedIds.Add(v.Model.Id);if(v.Model.GroupId!=null)foreach(var peer in Model.Elements.Where(a=>a.GroupId==v.Model.GroupId))selectedIds.Add(peer.Id); }
     Selected=views.Values.FirstOrDefault(v=>selectedIds.Contains(v.Model.Id));RefreshMultiSelection();e.Handled=true;
    }
   };
   SizeChanged+=delegate {if(View.Flow && IsLoaded) {double w=Model.PaperWidth,h=Model.PaperHeight;GrowTo(Math.Max(0,scroll.HorizontalOffset-200)/Scale+scroll.ViewportWidth/Scale,Math.Max(0,scroll.VerticalOffset-200)/Scale+scroll.ViewportHeight/Scale);if(w!=Model.PaperWidth||h!=Model.PaperHeight){Record();MarkDirty();}}};
  }
  public BitmapSource ImageSource(Element e) {
   var source=App.Store.Bitmap(e.AssetId,e.IsDrawing?8192:1600);
   if(!e.CropX.HasValue)return source;
   int x=(int)(e.CropX.Value*source.PixelWidth), y=(int)(e.CropY.Value*source.PixelHeight);
   int w=Math.Max(1,Math.Min(source.PixelWidth-x,(int)(e.CropWidth.Value*source.PixelWidth))), h=Math.Max(1,Math.Min(source.PixelHeight-y,(int)(e.CropHeight.Value*source.PixelHeight)));
   var crop=new CroppedBitmap(source,new Int32Rect(x,y,w,h));crop.Freeze();return crop;
  }
  public void RestoreImage(Element e) {if(e.Locked)return;Edit(delegate {e.ImageOpacity=null;e.CropX=e.CropY=e.CropWidth=e.CropHeight=null;});}
  public void EditImage(Element e) {if(e.Locked)return;EndEditing();Record();if(PropertyDialog.ImageEdit(this,e)){Model.SchemaVersion=Math.Max(3,Model.SchemaVersion);Render();Record();MarkDirty();}}
 }
}
