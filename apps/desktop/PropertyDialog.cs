using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace StickyCanvas
{
    public static class PropertyDialog
    {
        static Window Window(Window owner, string title, StackPanel panel)
        {
            var w = new Window { Owner = owner, Title = title, Width = 430, Height = 570, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = UI.Brush("#FFFFFBF1"), FontFamily = owner.FontFamily, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, ResizeMode = ResizeMode.CanResize };
            Native.ToolWindow(w); panel.Margin = new Thickness(18); return w;
        }
        static TextBox Field(Panel panel, string label, string value)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 3), Foreground = UI.Muted });
            var box = new TextBox { Text = value, Padding = new Thickness(6) }; panel.Children.Add(box); System.Windows.Automation.AutomationProperties.SetName(box, label); return box;
        }
        static double Read(TextBox box) { double n; if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out n) && !double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out n)) throw new FormatException("숫자를 입력해 주세요."); return n; }
        public static bool Edit(NoteWindow owner, Element e)
        {
            var panel = new StackPanel(); var w = Window(owner, "요소 위치·크기·서식", panel);
            panel.Children.Add(new TextBlock { Text = "요소 설정", FontSize = 20 });
            var x = Field(panel, "가로 위치 X", e.X.ToString("0.##")); var y = Field(panel, "세로 위치 Y", e.Y.ToString("0.##"));
            var width = Field(panel, "너비", e.Width.ToString("0.##")); var height = Field(panel, "높이", e.Height.ToString("0.##"));
            var fill = Field(panel, "채우기 #AARRGGBB (투명: #00FFFFFF)", e.Fill); var stroke = Field(panel, "테두리/선 색 #AARRGGBB", e.Stroke);
            var thickness = Field(panel, "테두리/선 두께 (0~20)", e.StrokeWidth.ToString("0.##"));
            var padding = Field(panel, "글상자 안쪽 여백 (0~40)", e.Padding.ToString("0.##"));
            var line = Field(panel, "줄 높이 (0=자동, 최대 300)", e.Paragraphs.Count == 0 ? "0" : e.Paragraphs[0].LineHeight.ToString("0.##"));
            panel.Children.Add(new TextBlock { Text = "이미지 크기는 비율을 유지합니다. 글상자 서식 버튼은 메모 상단의 Aa 서식에서 사용할 수 있습니다.", TextWrapping = TextWrapping.Wrap, Foreground = UI.Muted, Margin = new Thickness(0, 12, 0, 8) });
            panel.Children.Add(UI.Button("적용", delegate {
                try
                {
                    var candidate = Json.Copy(e); candidate.X = Read(x); candidate.Y = Read(y); candidate.Width = Read(width); candidate.Height = Read(height);
                    if (candidate.Type == "image") candidate.Height = candidate.Width * e.Height / e.Width;
                    candidate.Fill = fill.Text.Trim().ToUpperInvariant(); candidate.Stroke = stroke.Text.Trim().ToUpperInvariant(); candidate.StrokeWidth = Read(thickness); candidate.Padding = Read(padding);
                    double lineHeight = Read(line); Rules.Require(Rules.Number(lineHeight, 0, 300), "줄 높이는 0~300 범위여야 합니다."); foreach (var p in candidate.Paragraphs) p.LineHeight = lineHeight;
                    var test = Json.Copy(owner.Model); test.Elements[test.Elements.FindIndex(a => a.Id == candidate.Id)] = candidate; Rules.Validate(test);
                    e.X = candidate.X; e.Y = candidate.Y; e.Width = candidate.Width; e.Height = candidate.Height; e.Fill = candidate.Fill; e.Stroke = candidate.Stroke; e.StrokeWidth = candidate.StrokeWidth; e.Padding = candidate.Padding; e.Paragraphs = candidate.Paragraphs; w.DialogResult = true;
                }
                catch (Exception ex) { MemoApp.Error(ex); }
            }));
            return w.ShowDialog() == true;
        }
        public static bool PaperSize(Window owner, double width, double height, out double resultWidth, out double resultHeight)
        {
            var panel = new StackPanel(); var w = Window(owner, "메모 종이 크기", panel); w.Height = 340;
            var x = Field(panel, "종이 너비 (120~8192)", width.ToString("0.##")); var y = Field(panel, "종이 높이 (120~8192)", height.ToString("0.##"));
            panel.Children.Add(new TextBlock { Text = "창 크기와 별개입니다. 요소를 자동 축소하지 않으며, 요소가 잘리는 크기 변경은 거절합니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10), Foreground = UI.Muted });
            panel.Children.Add(UI.Button("적용", delegate { try { double a = Read(x), b = Read(y); Rules.Require(Rules.Number(a, 120, 8192) && Rules.Number(b, 120, 8192), "120~8192 범위의 숫자를 입력하세요."); w.DialogResult = true; } catch (Exception ex) { MemoApp.Error(ex); } }));
            bool ok = w.ShowDialog() == true; resultWidth = ok ? Read(x) : width; resultHeight = ok ? Read(y) : height; return ok;
        }
    }
}
