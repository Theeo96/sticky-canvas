using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace StickyCanvas
{
    public sealed partial class NoteWindow
    {
        readonly DispatcherTimer hideChromeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        DockPanel chromePanel;
        Button chromeToggle;
        bool presentationHover, suppressHover, saveFailed;
        int openMenus;
        string inkTool = "select";
        InkCanvas inkLayer;
        public bool ControlsVisible { get; private set; }
        public string InkTool { get { return inkTool; } }
        void InitializePresentation(Grid root, DockPanel panel, DockPanel header)
        {
            chromePanel = panel; chromePanel.VerticalAlignment = VerticalAlignment.Top; header.Margin = new Thickness(27, 0, 0, 0);
            chromePanel.Background = UI.Brush("#FFFFF9DF");
            chromePanel.SizeChanged += delegate { UpdateChromeInset(); };
            SizeChanged += delegate { UpdateChromeInset(); };
            chromeToggle = UI.Button("☰", delegate { SetUiPinned(!View.UiPinned); }, "도구 고정 표시 / 숨기기 · 마우스 오버로도 표시");
            chromeToggle.Width = 23; chromeToggle.Height = 23; chromeToggle.MinHeight = 23;
            chromeToggle.Padding = new Thickness(0); chromeToggle.Margin = new Thickness(2);
            chromeToggle.HorizontalAlignment = HorizontalAlignment.Left; chromeToggle.VerticalAlignment = VerticalAlignment.Top;
            root.Children.Add(chromeToggle);
            MouseEnter += delegate { hideChromeTimer.Stop(); suppressHover = false; NotifyHover(true); };
            MouseLeave += delegate { hideChromeTimer.Stop(); hideChromeTimer.Start(); };
            hideChromeTimer.Tick += delegate { hideChromeTimer.Stop(); if (!IsMouseOver && !IsMouseCaptureWithin) NotifyHover(false); };
            Deactivated += delegate { if (!IsMouseOver) NotifyHover(false); };
            LostMouseCapture += delegate { if (!IsMouseOver) NotifyHover(false); };
        }
        public void NotifyHover(bool value) { if (value && !presentationHover) suppressHover = false; presentationHover = value; RefreshChrome(); }
        public void SetUiPinned(bool value)
        {
            View.UiPinned = value; suppressHover = !value; RefreshChrome(); MarkDirty(false);
        }
        public void TrackMenu(ContextMenu menu)
        {
            menu.Opened += delegate { openMenus++; RefreshChrome(); };
            menu.Closed += delegate { openMenus = Math.Max(0, openMenus - 1); if (!IsMouseOver) NotifyHover(false); else RefreshChrome(); };
        }
        void RefreshChrome()
        {
            if (chromePanel == null) return;
            ControlsVisible = View.UiPinned || (presentationHover && View.HoverUi != "off" && !suppressHover) || openMenus > 0 || inkTool != "select" || saveFailed;
            chromePanel.Visibility = ControlsVisible ? Visibility.Visible : Visibility.Collapsed;
            chromeToggle.Content = View.UiPinned ? "▣" : "☰";
            chromeToggle.Opacity = ControlsVisible ? 1 : .45;
            scroll.HorizontalScrollBarVisibility = ControlsVisible ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
            scroll.VerticalScrollBarVisibility = ControlsVisible ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
            if (Selected != null) Selected.SetSelected(ControlsVisible);
            if (ActiveText != null && ActiveText.Editor != null) ActiveText.Editor.VerticalScrollBarVisibility = ControlsVisible ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
            UpdateChromeInset();
        }
        void UpdateChromeInset()
        {
            if (chromePanel == null) return;
            double height = ControlsVisible ? Math.Min(chromePanel.ActualHeight, Math.Max(0, ActualHeight - 80)) : 0;
            var margin = new Thickness(0, height, 0, 0);
            if (scroll.Margin != margin) scroll.Margin = margin;
        }
        void BuildInkTools(WrapPanel toolbar)
        {
            toolbar.Children.Add(UI.Button("선택", delegate { SetInkTool("select"); }, "요소 선택 / 그리기 종료 (Esc)"));
            toolbar.Children.Add(UI.Button("✎ 펜", delegate { SetInkTool("pen"); }, "마우스·펜으로 손그림"));
            toolbar.Children.Add(UI.Button("지우개", delegate { SetInkTool("erase"); }, "펜 선 전체 지우기 — 글·사진·도형은 유지"));
            toolbar.Children.Add(UI.Button("펜 색", delegate { PickColor(View.PenColor ?? "#FF3E392F", delegate(string c) { View.PenColor = c; ConfigureInkAttributes(); MarkDirty(false); }); }));
            var size = new ComboBox { Width = 48, Margin = new Thickness(3), ToolTip = "펜 굵기" };
            foreach (int n in new[] { 1, 2, 3, 5, 8, 12, 16 }) size.Items.Add(n);
            size.SelectedItem = View.PenWidth > 0 ? (int)View.PenWidth : 3;
            size.SelectionChanged += delegate { if (size.SelectedItem != null) { View.PenWidth = (int)size.SelectedItem; ConfigureInkAttributes(); MarkDirty(false); } };
            toolbar.Children.Add(size);
            Paper.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (inkTool == "erase") { EraseInkAt(e.GetPosition(Paper)); e.Handled = true; } };
            Paper.PreviewMouseMove += delegate(object sender, MouseEventArgs e) { if (inkTool == "erase" && e.LeftButton == MouseButtonState.Pressed) { EraseInkAt(e.GetPosition(Paper)); e.Handled = true; } };
        }
        public void SetInkTool(string tool)
        {
            if (tool != "select" && tool != "pen" && tool != "erase") throw new ArgumentException("Unknown tool");
            EndEditing(); Select(null); inkTool = tool; ConfigureInkOverlay(); RefreshChrome();
            Paper.Cursor = tool == "select" ? Cursors.Arrow : Cursors.Pen;
        }
        void ExitInkForObjectAction() { if (inkTool != "select") SetInkTool("select"); }
        void ConfigureInkAttributes()
        {
            if (inkLayer == null) return;
            double width = View.PenWidth > 0 ? View.PenWidth : 3;
            inkLayer.DefaultDrawingAttributes = new DrawingAttributes { Color = (Color)ColorConverter.ConvertFromString(View.PenColor ?? "#FF3E392F"), Width = width, Height = width, IgnorePressure = true, FitToCurve = false };
        }
        void ConfigureInkOverlay()
        {
            if (inkLayer != null) Paper.Children.Remove(inkLayer);
            inkLayer = null;
            if (inkTool != "pen") return;
            inkLayer = new InkCanvas { Width = Model.PaperWidth, Height = Model.PaperHeight, Background = Brushes.Transparent, ClipToBounds = true, EditingMode = InkCanvasEditingMode.Ink };
            ConfigureInkAttributes();
            inkLayer.StrokeCollected += delegate(object sender, InkCanvasStrokeCollectedEventArgs e) { CommitInk(e.Stroke); };
            Paper.Children.Add(inkLayer);
        }
        public void CommitInk(Stroke stroke)
        {
            var element = InkDrawing.FromStroke(stroke, Model.PaperWidth, Model.PaperHeight);
            Edit(delegate { Model.SchemaVersion = 2; Model.Elements.Add(element); });
        }
        public bool EraseInkAt(Point point)
        {
            var hit = Model.Elements.AsEnumerable().Reverse().FirstOrDefault(e => e.Type == "stroke" && InkDrawing.ToStroke(e, e.Width, e.Height).HitTest(new Point(point.X - e.X, point.Y - e.Y), Math.Max(8, e.StrokeWidth + 4)));
            if (hit == null) return false;
            Edit(delegate { Model.Elements.RemoveAll(e => e.Id == hit.Id); }); return true;
        }
    }
    public static class InkDrawing
    {
        public static Element FromStroke(Stroke stroke, double paperWidth, double paperHeight)
        {
            var points = stroke.StylusPoints; if (points.Count == 0) throw new ArgumentException("빈 선입니다.");
            double thickness = Math.Max(1, Math.Min(16, stroke.DrawingAttributes.Width)), pad = thickness / 2;
            int stride = Math.Max(1, (int)Math.Ceiling(points.Count / 19999.0));
            var sampled = new List<Point>();
            for (int i = 0; i < points.Count; i += stride) sampled.Add(new Point(Math.Max(pad, Math.Min(paperWidth - pad, points[i].X)), Math.Max(pad, Math.Min(paperHeight - pad, points[i].Y))));
            var last = points[points.Count - 1]; sampled.Add(new Point(Math.Max(pad, Math.Min(paperWidth - pad, last.X)), Math.Max(pad, Math.Min(paperHeight - pad, last.Y))));
            double x = Math.Max(0, sampled.Min(p => p.X) - pad), y = Math.Max(0, sampled.Min(p => p.Y) - pad);
            double width = Math.Max(16, sampled.Max(p => p.X) + pad - x), height = Math.Max(16, sampled.Max(p => p.Y) + pad - y);
            x = Math.Min(x, paperWidth - width); y = Math.Min(y, paperHeight - height);
            return new Element { Type = "stroke", X = x, Y = y, Width = width, Height = height, Stroke = stroke.DrawingAttributes.Color.ToString(), StrokeWidth = thickness, Points = sampled.Select(p => new InkPoint { X = (p.X - x) / width, Y = (p.Y - y) / height }).ToList() };
        }
        public static Stroke ToStroke(Element element, double width, double height)
        {
            var points = new StylusPointCollection(element.Points.Select(p => new StylusPoint(p.X * width, p.Y * height)));
            double penWidth = Math.Max(1, element.StrokeWidth);
            return new Stroke(points, new DrawingAttributes { Color = (Color)ColorConverter.ConvertFromString(element.Stroke), Width = penWidth, Height = penWidth, IgnorePressure = true, FitToCurve = false });
        }
    }
}
