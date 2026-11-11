using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace StickyCanvas {
 public sealed partial class NoteWindow {
  PenPointerVisual penPointer;
  Grid pointerRoot;
  public double PenPointerDiameter {get{return (View.PenWidth>0?View.PenWidth:3)*Scale;}}
  public double PenLocatorDiameter {get{return Math.Max(20,PenPointerDiameter+12);}}
  void InitializePenPointer(Grid root) {
   pointerRoot=root;penPointer=new PenPointerVisual{IsHitTestVisible=false,Visibility=Visibility.Hidden,ClipToBounds=true};root.Children.Add(penPointer);
   PreviewMouseMove+=delegate{RefreshPenPointer();};MouseLeave+=delegate{penPointer.Visibility=Visibility.Hidden;};Deactivated+=delegate{penPointer.Visibility=Visibility.Hidden;};
  }
  void RefreshPenPointer() {
   if(penPointer==null)return;
   var point=Mouse.GetPosition(Paper);
   bool show=inkTool=="pen" && IsMouseOver && IsActive && IsInside(Paper,Mouse.DirectlyOver as DependencyObject) && point.X>=0 && point.Y>=0 && point.X<Model.PaperWidth && point.Y<Model.PaperHeight;
   penPointer.Visibility=show?Visibility.Visible:Visibility.Hidden;
   if(show)penPointer.Update(Mouse.GetPosition(pointerRoot),PenPointerDiameter,PenLocatorDiameter,CurrentPenColor);
  }
 }
 sealed class PenPointerVisual : FrameworkElement {
  Point center;double size,locator;string color;Brush fill=Brushes.Black;
  static readonly Pen dark=new Pen(Brushes.Black,3),light=new Pen(Brushes.White,1);
  public void Update(Point position,double diameter,double outerDiameter,string penColor) {
   center=position;size=diameter;locator=outerDiameter;if(color!=penColor){color=penColor;fill=UI.Brush(penColor);}InvalidateVisual();
  }
  protected override void OnRender(DrawingContext dc) {
   base.OnRender(dc);double radius=locator/2;
   dc.DrawEllipse(null,dark,center,radius,radius);dc.DrawEllipse(null,light,center,radius,radius);
   if(size<=4)dc.DrawEllipse(fill,null,center,size/2,size/2);
   else {dc.DrawEllipse(null,dark,center,size/2,size/2);dc.DrawEllipse(null,light,center,size/2,size/2);dc.DrawEllipse(fill,null,center,1,1);}
  }
 }
}
