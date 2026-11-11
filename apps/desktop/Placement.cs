using System;
using System.Windows;
using System.Windows.Controls;

namespace StickyCanvas
{
    public sealed partial class NoteWindow
    {
        public bool FormatsVisible { get { return formats.Visibility == Visibility.Visible; } }
        public bool ToolsAtTop { get { return chromePanel.VerticalAlignment == VerticalAlignment.Top; } }
        public string FormatToggleLabel { get { return Convert.ToString(formatToggle.Content); } }
        public void ToggleFormats()
        {
            ExitInkForObjectAction();
            formats.Visibility = FormatsVisible ? Visibility.Collapsed : Visibility.Visible;
            formatToggle.Content = FormatsVisible ? "Aa 서식 ▴" : "Aa 서식 ▾";
        }
        public double SnapPosition(double position, double maximum)
        {
            if (View.SnapGrid) position = Math.Round(position / 8) * 8;
            return Math.Max(0, Math.Min(maximum, position));
        }
        public void AlignElement(string alignment)
        {
            ExitInkForObjectAction();
            if (Selected == null || SelectedLocked) return;
            var element = Selected.Model;
            Edit(delegate {
                switch (alignment)
                {
                    case "left": element.X = 0; break;
                    case "center": element.X = (Model.PaperWidth - element.Width) / 2; break;
                    case "right": element.X = Model.PaperWidth - element.Width; break;
                    case "top": element.Y = 0; break;
                    case "middle": element.Y = (Model.PaperHeight - element.Height) / 2; break;
                    case "bottom": element.Y = Model.PaperHeight - element.Height; break;
                    default: throw new ArgumentException("Unknown alignment");
                }
            });
        }
        void PlacementMenu()
        {
            ExitInkForObjectAction(); var menu = new ContextMenu();
            foreach (var pair in new[] { new[] { "종이 왼쪽에 맞춤", "left" }, new[] { "종이 가로 가운데", "center" }, new[] { "종이 오른쪽에 맞춤", "right" }, new[] { "종이 위쪽에 맞춤", "top" }, new[] { "종이 세로 가운데", "middle" }, new[] { "종이 아래쪽에 맞춤", "bottom" } })
            {
                string action = pair[1]; var item = new MenuItem { Header = pair[0], IsEnabled = Selected != null };
                item.Click += delegate { AlignElement(action); }; menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            var snap = new MenuItem { Header = "이동 시 8칸 격자에 맞춤", IsCheckable = true, IsChecked = View.SnapGrid };
            snap.Click += delegate { View.SnapGrid = snap.IsChecked; MarkDirty(false); }; menu.Items.Add(snap);
            TrackMenu(menu); menu.IsOpen = true;
        }
    }
}
