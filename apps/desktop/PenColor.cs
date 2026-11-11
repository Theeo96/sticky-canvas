using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace StickyCanvas {
 public sealed partial class NoteWindow {
  Border penColorSwatch;
  Button penColorButton,eyedropperButton;
  public string CurrentPenColor {get{return View.PenColor??"#FF3E392F";}}
  public Color SwatchColor {get{return ((SolidColorBrush)penColorSwatch.Background).Color;}}
  public void SetPenColor(string color) {
   Rules.Require(Rules.Color(color),"펜 색상 형식이 올바르지 않습니다.");
   View.PenColor=color.ToUpperInvariant();ConfigureInkAttributes();RefreshPenColorSwatch();MarkDirty(false);
  }
  void RefreshPenColorSwatch() {
   if(penColorSwatch==null)return;penColorSwatch.Background=UI.Brush(CurrentPenColor);
   penColorButton.ToolTip="펜 색 선택 · 현재 "+CurrentPenColor;
   System.Windows.Automation.AutomationProperties.SetName(penColorButton,"펜 색 "+CurrentPenColor);
   if(eyedropperButton!=null)eyedropperButton.Content=inkTool=="pick"?"⌖ 스포이드 ●":"⌖ 스포이드";
  }
  void BuildPenColorTools(WrapPanel toolbar) {
   penColorSwatch=new Border{Width=17,Height=17,Margin=new Thickness(0,0,5,0),BorderBrush=Brushes.Gray,BorderThickness=new Thickness(1),Background=UI.Brush(CurrentPenColor)};
   var label=new StackPanel{Orientation=Orientation.Horizontal};label.Children.Add(penColorSwatch);label.Children.Add(new TextBlock{Text="펜 색",VerticalAlignment=VerticalAlignment.Center});
   penColorButton=UI.Button("펜 색",delegate{if(inkTool=="pick")SetInkTool("select");PickColor(CurrentPenColor,SetPenColor);});penColorButton.Content=label;toolbar.Children.Add(penColorButton);
   eyedropperButton=UI.Button("⌖ 스포이드",delegate{if(inkTool=="pick")SetInkTool("select");else{FinishImageEdit(true);SetInkTool("pick");}},"메모 안의 보이는 색을 클릭해 펜 색으로 사용 · Esc/우클릭 취소");toolbar.Children.Add(eyedropperButton);RefreshPenColorSwatch();
   PreviewMouseDown+=delegate(object sender,MouseButtonEventArgs e) {
    if(inkTool!="pick")return;
    if(e.ChangedButton==MouseButton.Right){SetInkTool("select");e.Handled=true;return;}
    if(e.ChangedButton!=MouseButton.Left||!IsInside(Paper,e.OriginalSource as DependencyObject))return;
    var point=e.GetPosition(Paper);
    if(point.X<0||point.Y<0||point.X>=Model.PaperWidth||point.Y>=Model.PaperHeight)return;
    try {PickPenColorAt(point);}catch(Exception ex){SetInkTool("select");MemoApp.Error(ex);}e.Handled=true;
   };
   PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(inkTool=="pick"&&e.Key==Key.Escape){SetInkTool("select");e.Handled=true;}};
  }
  public void PickPenColorAt(Point point) {SetPenColor(SamplePaperColor(point).ToString());SetInkTool("pen");}
  public Color SamplePaperColor(Point point) {
   if(point.X<0||point.Y<0||point.X>=Model.PaperWidth||point.Y>=Model.PaperHeight)throw new ArgumentOutOfRangeException("point");
   var frameVisibility=selectionFrame==null?Visibility.Collapsed:selectionFrame.Visibility;
   var inkVisibility=inkLayer==null?Visibility.Collapsed:inkLayer.Visibility;
   try {
    foreach(var view in views.Values)view.ShowSelectionHandles(false);
    if(selectionFrame!=null)selectionFrame.Visibility=Visibility.Hidden;
    if(inkLayer!=null)inkLayer.Visibility=Visibility.Hidden;
    UpdateLayout();
    var brush=new VisualBrush(Paper){Stretch=Stretch.Fill,ViewboxUnits=BrushMappingMode.RelativeToBoundingBox,Viewbox=new Rect(0,0,1,1)};
    var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawRectangle(brush,null,new Rect(-Math.Floor(point.X),-Math.Floor(point.Y),Paper.ActualWidth,Paper.ActualHeight));
    var bitmap=new RenderTargetBitmap(1,1,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
    var pixel=new byte[4];bitmap.CopyPixels(pixel,4,0);int white=255-pixel[3];
    return Color.FromRgb((byte)Math.Min(255,pixel[2]+white),(byte)Math.Min(255,pixel[1]+white),(byte)Math.Min(255,pixel[0]+white));
   } finally {
    foreach(var view in views.Values)view.ShowSelectionHandles(true);
    if(selectionFrame!=null)selectionFrame.Visibility=frameVisibility;
    if(inkLayer!=null)inkLayer.Visibility=inkVisibility;
   }
  }
 }
}
