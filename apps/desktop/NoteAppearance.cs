using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
namespace StickyCanvas {
 public sealed partial class NoteWindow {
  Grid appearanceRoot;
  Border noteOpacityBar;
  public IEnumerable<Element> FindableElements {get{return Model.Elements.Where(e=>e.Type!="stroke"&&!e.IsDrawing);}}
  public double NoteOpacity {get{return View.WindowOpacity??1;}}
  void InitializeNoteOpacity(Grid root) {
   appearanceRoot=root;
   SizeChanged+=delegate{if(noteOpacityBar!=null)noteOpacityBar.MaxWidth=Math.Max(200,ActualWidth-20);};
   PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(noteOpacityBar!=null&&e.Key==Key.Escape){CloseNoteOpacity();e.Handled=true;}};
  }
  public void SetNoteOpacity(double opacity) {
   Rules.Require(Rules.Number(opacity,.2,1),"메모 불투명도는 20~100% 범위여야 합니다.");
   Opacity=opacity;
   View.WindowOpacity=opacity;MarkDirty(false);
  }
  void CloseNoteOpacity() {if(noteOpacityBar!=null)appearanceRoot.Children.Remove(noteOpacityBar);noteOpacityBar=null;}
  void ShowNoteOpacity() {
   FinishImageEdit(true);SetInkTool("select");CloseNoteOpacity();
   var panel=new WrapPanel();panel.Children.Add(new TextBlock{Text="메모 전체 불투명도",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4)});
   var slider=new Slider{Minimum=20,Maximum=100,Value=NoteOpacity*100,Width=140,SmallChange=1,LargeChange=10,TickFrequency=1,IsSnapToTickEnabled=true,Margin=new Thickness(6),ToolTip="20~100% · 낮을수록 바탕화면이 비칩니다"};
   System.Windows.Automation.AutomationProperties.SetName(slider,"메모 전체 불투명도");
   var label=new TextBlock{Text=slider.Value.ToString("0")+"%",Width=42,VerticalAlignment=VerticalAlignment.Center};
   slider.ValueChanged+=delegate{SetNoteOpacity(slider.Value/100);label.Text=slider.Value.ToString("0")+"%";};panel.Children.Add(slider);panel.Children.Add(label);
   panel.Children.Add(UI.Button("100%",delegate{slider.Value=100;}));panel.Children.Add(UI.Button("닫기",CloseNoteOpacity));
   noteOpacityBar=new Border{Child=panel,Background=UI.Brush(Model.Background),BorderBrush=ChromeForeground,BorderThickness=new Thickness(1),Padding=new Thickness(5),Margin=new Thickness(4),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom,MaxWidth=Math.Max(200,ActualWidth-20)};
   ThemeTree(noteOpacityBar,UI.Brush(Model.Background));appearanceRoot.Children.Add(noteOpacityBar);
  }
 }
}
