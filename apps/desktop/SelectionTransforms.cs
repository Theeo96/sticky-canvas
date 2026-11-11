using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
namespace StickyCanvas {
 public sealed partial class NoteWindow {
  bool transformGesture;
  public void SetTransformGesture(bool active){transformGesture=active;}
  Grid selectionFrame;
  void UpdateSelectionFrame() {
   var elements=Model.Elements.Where(e=>selectedIds.Contains(e.Id)).ToList();
   if(elements.Count<2||!ControlsVisible){if(selectionFrame!=null)Paper.Children.Remove(selectionFrame);selectionFrame=null;return;}
   if(selectionFrame==null||!Paper.Children.Contains(selectionFrame)) {
    selectionFrame=new Grid();selectionFrame.Children.Add(new Border{BorderBrush=Brushes.SteelBlue,BorderThickness=new Thickness(1),IsHitTestVisible=false});Paper.Children.Add(selectionFrame);
    if(!SelectedLocked) foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1}) {
     int sx=x,sy=y;Point start=new Point();
     var thumb=new Thumb{Width=12,Height=12,Background=Brushes.White,BorderBrush=Brushes.SteelBlue,BorderThickness=new Thickness(2),HorizontalAlignment=x<0?HorizontalAlignment.Left:HorizontalAlignment.Right,VerticalAlignment=y<0?VerticalAlignment.Top:VerticalAlignment.Bottom,Cursor=x==y?Cursors.SizeNWSE:Cursors.SizeNESW,ToolTip="선택 전체 비율 유지 크기 조절"};
     thumb.DragStarted+=delegate{SetTransformGesture(true);EndEditing();Record();StartGroupDrag(elements[0]);start=Mouse.GetPosition(Paper);};
     thumb.DragDelta+=delegate{var delta=Mouse.GetPosition(Paper)-start;ResizeSelection(delta.X,delta.Y,sx,sy);MarkDirty();};
     thumb.DragCompleted+=delegate{SetTransformGesture(false);Record();};selectionFrame.Children.Add(thumb);
    }
   }
   double left=elements.Min(e=>e.X),top=elements.Min(e=>e.Y);Canvas.SetLeft(selectionFrame,left);Canvas.SetTop(selectionFrame,top);selectionFrame.Width=elements.Max(e=>e.X+e.Width)-left;selectionFrame.Height=elements.Max(e=>e.Y+e.Height)-top;
  }
  List<Element> resizeSnapshot;
  public bool SelectedLocked { get {return Model.Elements.Any(e=>selectedIds.Contains(e.Id)&&e.Locked) || (Selected!=null&&Selected.Model.Locked);}}
  public void LockSelection(bool locked) {if(selectedIds.Count==0)return;Edit(delegate{Model.SchemaVersion=Math.Max(4,Model.SchemaVersion);foreach(var e in Model.Elements.Where(e=>selectedIds.Contains(e.Id)))e.Locked=locked;});}
  public void ResizeSelection(double dx,double dy,int sideX,int sideY) {
   if(SelectedLocked||resizeSnapshot==null||resizeSnapshot.Count==0)return;
   double left=resizeSnapshot.Min(e=>e.X),top=resizeSnapshot.Min(e=>e.Y),right=resizeSnapshot.Max(e=>e.X+e.Width),bottom=resizeSnapshot.Max(e=>e.Y+e.Height);
   double width=right-left,height=bottom-top;
   double factor=Math.Max(1+dx*sideX/width,1+dy*sideY/height);
   double min=resizeSnapshot.Max(e=>Math.Max(16/e.Width,16/e.Height));
   double max= Math.Min((sideX<0?right:Model.PaperWidth-left)/width,(sideY<0?bottom:Model.PaperHeight-top)/height);
   foreach(var run in resizeSnapshot.SelectMany(e=>e.Paragraphs).SelectMany(p=>p.Runs)) {min=Math.Max(min,8/run.Size);max=Math.Min(max,144/run.Size);}
   foreach(var e in resizeSnapshot){if(e.StrokeWidth>0)max=Math.Min(max,20/e.StrokeWidth);if(e.Padding>0)max=Math.Min(max,40/e.Padding);}
   factor=Math.Max(min,Math.Min(max,factor));double x=sideX<0?right-width*factor:left,y=sideY<0?bottom-height*factor:top;
   foreach(var original in resizeSnapshot) {var e=Model.Elements.First(a=>a.Id==original.Id);e.X=x+(original.X-left)*factor;e.Y=y+(original.Y-top)*factor;e.Width=original.Width*factor;e.Height=original.Height*factor;e.StrokeWidth=original.StrokeWidth*factor;e.Padding=original.Padding*factor;
    e.Paragraphs=Json.Copy(original).Paragraphs;foreach(var p in e.Paragraphs){if(p.LineHeight>0)p.LineHeight=Math.Min(300,p.LineHeight*factor);foreach(var r in p.Runs)r.Size*=factor;}
    views[e.Id].Layout();views[e.Id].RefreshBody();
   }
   UpdateSelectionFrame();
  }
  public void Distribute(bool vertical) {
   if(SelectedLocked)return;
   var units=Model.Elements.Where(e=>selectedIds.Contains(e.Id)).GroupBy(e=>e.GroupId??e.Id).Select(g=>g.ToList()).OrderBy(g=>g.Min(e=>vertical?e.Y:e.X)).ToList();if(units.Count<3)return;
   Func<List<Element>,double> start=g=>g.Min(e=>vertical?e.Y:e.X);
   Func<List<Element>,double> end=g=>g.Max(e=>vertical?e.Y+e.Height:e.X+e.Width);
   double gap=(end(units.Last())-start(units.First())-units.Sum(g=>end(g)-start(g)))/(units.Count-1);
   Edit(delegate {double next=end(units[0])+gap;for(int i=1;i<units.Count-1;i++){double delta=next-start(units[i]);foreach(var e in units[i]){if(vertical)e.Y+=delta;else e.X+=delta;}next=end(units[i])+gap;}});
  }
 }
}
