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
        DockPanel chromePanel;
        Button chromeToggle, saveAlert;
        bool saveFailed;
        string inkTool = "select";
        InkCanvas inkLayer;
        public bool ControlsVisible { get; private set; }
        public bool SaveWarningVisible { get { return saveAlert != null && saveAlert.Visibility == Visibility.Visible; } }
        public string InkTool { get { return inkTool; } }
        void InitializePresentation(Grid root, DockPanel panel, DockPanel header)
        {
            chromePanel = panel; chromePanel.VerticalAlignment = VerticalAlignment.Top;
            header.Margin = new Thickness(27, 0, 0, 0);
            chromeToggle = UI.Button("☰", delegate { SetUiPinned(!View.UiPinned); }, "상단 도구 표시 / 숨기기");
            chromeToggle.Width = 23; chromeToggle.Height = 23; chromeToggle.MinHeight = 23;
            chromeToggle.Padding = new Thickness(0); chromeToggle.Margin = new Thickness(2);
            chromeToggle.HorizontalAlignment = HorizontalAlignment.Left; chromeToggle.VerticalAlignment = VerticalAlignment.Top;
            root.Children.Add(chromeToggle);
            saveAlert = UI.Button("저장 실패 · 재시도", delegate { Flush(true); }, "저장하지 못했습니다. 클릭해 재시도하세요.");
            saveAlert.HorizontalAlignment = HorizontalAlignment.Right; saveAlert.VerticalAlignment = VerticalAlignment.Bottom;
            root.Children.Add(saveAlert);
            InitializeHeaderDrag(header);
        }
        public void SetUiPinned(bool value)
        {
            View.UiPinned = value; if (!value) FinishTitleEdit(); RefreshChrome(); MarkDirty(false);
        }
        public void TrackMenu(ContextMenu menu)
        {
            menu.Opened += delegate { ApplyMenuTheme(menu); };
        }
        void RefreshChrome()
        {
            if (chromePanel == null) return;
            ControlsVisible = View.UiPinned;
            chromePanel.Visibility = ControlsVisible ? Visibility.Visible : Visibility.Collapsed;
            chromeToggle.Content = ControlsVisible ? "▣" : "☰";
            chromeToggle.Opacity = 1;
            saveAlert.Visibility = saveFailed && !ControlsVisible ? Visibility.Visible : Visibility.Collapsed;
            // Overlay controls never change the paper viewport, offsets or fit scale.
            scroll.Margin = new Thickness(0);
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            RefreshMultiSelection();
            ApplyChromeTheme();
        }
        void BuildInkTools(WrapPanel toolbar)
        {
            toolbar.Children.Add(UI.Button("↖ 선택", delegate { SetInkTool("select"); }, "요소 선택 / 그리기 종료 (Esc)"));
            toolbar.Children.Add(UI.Button("✎ 펜", delegate { SetInkTool("pen"); }, "마우스·펜으로 손그림"));
            toolbar.Children.Add(UI.Button("▰ 지우개", delegate { SetInkTool("erase"); }, "픽셀 필기의 닿는 부분 지우기 — 글·사진·도형은 유지"));
            BuildPenColorTools(toolbar);
            BuildBrushSliders(toolbar);
            Paper.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (inkTool == "erase") { BeginPixelErase(); ErasePixelAt(e.GetPosition(Paper)); Paper.CaptureMouse(); e.Handled = true; } };
            Paper.PreviewMouseMove += delegate(object sender, MouseEventArgs e) { if (inkTool == "erase" && e.LeftButton == MouseButtonState.Pressed) { ErasePixelAt(e.GetPosition(Paper)); e.Handled = true; } };
            Paper.PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) { if (inkTool == "erase") {FinishPixelErase();Paper.ReleaseMouseCapture();e.Handled=true;} };
            Paper.LostMouseCapture += delegate { FinishPixelErase(); };
        }
        public void SetInkTool(string tool)
        {
            if (tool != "select" && tool != "pen" && tool != "erase" && tool != "pick") throw new ArgumentException("Unknown tool");
            FinishPixelErase(); EndEditing(); Select(null); inkTool = tool; ConfigureInkOverlay(); RefreshChrome();
            Paper.Cursor = tool == "pick" ? Cursors.Cross : tool == "select" ? Cursors.Arrow : Cursors.Pen;
            RefreshPenColorSwatch();
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
            RasterizeDrawing(element);
            Edit(delegate { Model.SchemaVersion = Math.Max(4,Model.SchemaVersion); Model.Elements.Add(element); });
        }
        public bool EraseInkAt(Point point)
        {
            BeginPixelErase();bool changed=ErasePixelAt(point);FinishPixelErase();return changed;
        }

    }
    public static class InkDrawing
    {
        public static Element FromStroke(Stroke stroke, double paperWidth, double paperHeight)
        {
            var points = stroke.StylusPoints; if (points.Count == 0) throw new ArgumentException("빈 선입니다.");
            double thickness = Math.Max(1, Math.Min(64, stroke.DrawingAttributes.Width)), pad = thickness / 2;
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
