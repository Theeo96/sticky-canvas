using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace StickyCanvas
{
    public sealed class ElementView : Grid
    {
        public readonly Element Model;
        readonly NoteWindow owner; readonly Grid body = new Grid(); readonly Grid handles = new Grid();
        public RichTextBox Editor { get; private set; }
        public ElementView(NoteWindow owner, Element model)
        {
            this.owner = owner; Model = model; ClipToBounds = false;
            Children.Add(body); Children.Add(handles); RefreshBody(); SetSelected(false); Layout();
            MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
                if (Editor != null) { Editor.Focus(); Keyboard.Focus(Editor); return; }
                owner.Select(this);
                if (e.ClickCount == 2 && Model.Type == "text") BeginEdit();
                e.Handled = true;
            };
            var menu = new ContextMenu();
            Action<string, Action> add = delegate(string title, Action action) { var item = new MenuItem { Header = title }; item.Click += delegate { owner.Select(this); action(); }; menu.Items.Add(item); };
            add("글 편집", delegate { if (Model.Type == "text") BeginEdit(); });
            add("위치·크기·서식…", owner.Properties);
            add("앞으로", delegate { owner.Reorder(1); }); add("뒤로", delegate { owner.Reorder(-1); });
            add("맨 앞으로", delegate { owner.Reorder(10000); }); add("맨 뒤로", delegate { owner.Reorder(-10000); });
            add("복제", owner.Duplicate); add("삭제", owner.Delete);
            if (Model.Type == "text") add("내용에 맞춰 높이 늘리기", owner.FitTextHeight);
            if (Model.Type == "line" || Model.Type == "arrow")
            {
                add("화살표 시작·끝 반전", delegate { owner.Edit(delegate { Model.ReverseLine = !Model.ReverseLine; }); });
                foreach (var direction in new[] { new[] { "가로 선", "horizontal" }, new[] { "세로 선", "vertical" }, new[] { "↘ 대각선", "down" }, new[] { "↗ 대각선", "up" } })
                {
                    string orientation = direction[1]; add(direction[0], delegate { owner.Edit(delegate { Model.LineOrientation = orientation; }); });
                }
            }
            owner.TrackMenu(menu); ContextMenu = menu;
        }
        public void Layout()
        {
            Width = Model.Width; Height = Model.Height; Canvas.SetLeft(this, Model.X); Canvas.SetTop(this, Model.Y);
            if (body.Children.Count > 0 && body.Children[0] is ElementDrawing) ((ElementDrawing)body.Children[0]).InvalidateVisual();
        }
        public void RefreshBody()
        {
            body.Children.Clear(); Editor = null;
            if (Model.Type == "text")
            {
                var border = new Border { Background = UI.Brush(Model.Fill), BorderBrush = UI.Brush(Model.Stroke), BorderThickness = new Thickness(Model.StrokeWidth), ClipToBounds = true };
                var text = TextCodec.Display(Model); border.Child = text; body.Children.Add(border);
                text.Measure(new Size(Math.Max(1, Model.Width - Model.StrokeWidth * 2), double.PositiveInfinity));
                if (text.DesiredSize.Height > Model.Height - Model.StrokeWidth * 2 + 1)
                    body.Children.Add(new TextBlock { Text = "⋯", FontSize = 16, Background = UI.Brush("#FFFFE7C0"), Foreground = UI.Brush("#FF8D4A15"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, ToolTip = "글이 넘칩니다. 우클릭 → 내용에 맞춰 높이 늘리기", IsHitTestVisible = false });
            }
            else if (Model.Type == "image")
            {
                try { body.Children.Add(new Image { Source = owner.App.Store.Bitmap(Model.AssetId), Stretch = Stretch.Uniform, IsHitTestVisible = false }); }
                catch { body.Children.Add(new TextBlock { Text = "이미지를 읽을 수 없습니다\n원본 데이터는 보존됩니다", TextWrapping = TextWrapping.Wrap, Foreground = UI.Muted, Background = UI.Brush("#FFF3E7D2") }); }
                Background = Brushes.Transparent;
            }
            else { body.Children.Add(new ElementDrawing(Model)); Background = Brushes.Transparent; }
        }
        public void SetSelected(bool selected)
        {
            selected = selected && owner.ControlsVisible;
            handles.Children.Clear(); if (!selected) return;
            handles.Children.Add(new Border { BorderBrush = UI.Brush("#FF4C81B8"), BorderThickness = new Thickness(1), IsHitTestVisible = false });
            var move = new Thumb { Height = 12, VerticalAlignment = VerticalAlignment.Top, Background = UI.Brush("#99537FB1"), Cursor = Cursors.SizeAll, ToolTip = "이 손잡이를 끌어 요소 이동" };
            System.Windows.Automation.AutomationProperties.SetName(move, "선택 요소 이동"); handles.Children.Add(move); WireDrag(move, 0, 0);
            foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 })
            {
                var handle = new Thumb { Width = 10, Height = 10, HorizontalAlignment = x < 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right, VerticalAlignment = y < 0 ? VerticalAlignment.Top : VerticalAlignment.Bottom, Background = Brushes.White, BorderBrush = UI.Brush("#FF4C81B8"), BorderThickness = new Thickness(1), Cursor = x == y ? Cursors.SizeNWSE : Cursors.SizeNESW, ToolTip = "요소 크기 조절" };
                handles.Children.Add(handle); WireDrag(handle, x, y);
            }
        }
        void WireDrag(Thumb thumb, int sideX, int sideY)
        {
            Point start = new Point(); double x = 0, y = 0, width = 0, height = 0;
            thumb.DragStarted += delegate {
                owner.EndEditing(); owner.Record(); start = Mouse.GetPosition(owner.Paper); x = Model.X; y = Model.Y; width = Model.Width; height = Model.Height;
            };
            thumb.DragDelta += delegate {
                var p = Mouse.GetPosition(owner.Paper); double dx = p.X - start.X, dy = p.Y - start.Y;
                if (sideX == 0)
                {
                    Model.X = owner.SnapPosition(x + dx, owner.Model.PaperWidth - width);
                    Model.Y = owner.SnapPosition(y + dy, owner.Model.PaperHeight - height);
                }
                else
                {
                    double w = Math.Max(16, width + dx * sideX), h = Math.Max(16, height + dy * sideY);
                    double maxW = sideX < 0 ? x + width : owner.Model.PaperWidth - x;
                    double maxH = sideY < 0 ? y + height : owner.Model.PaperHeight - y;
                    if (Model.Type == "image")
                    {
                        double factor = Math.Max(w / width, h / height);
                        factor = Math.Min(factor, Math.Min(maxW / width, maxH / height));
                        factor = Math.Max(Math.Max(16 / width, 16 / height), factor); w = width * factor; h = height * factor;
                    }
                    w = Math.Min(maxW, w); h = Math.Min(maxH, h);
                    Model.Width = w; Model.Height = h; Model.X = sideX < 0 ? x + width - w : x; Model.Y = sideY < 0 ? y + height - h : y;
                }
                Layout(); owner.MarkDirty();
            };
            thumb.DragCompleted += delegate { owner.Record(); RefreshBody(); owner.UpdateSelectionInfo(); };
        }
        public void BeginEdit()
        {
            if (Model.Type != "text" || Editor != null) return;
            owner.EndEditing(); owner.Record(); owner.Select(this); body.Children.Clear();
            Editor = new RichTextBox { Document = TextCodec.Document(Model), BorderThickness = new Thickness(Model.StrokeWidth), BorderBrush = UI.Brush(Model.Stroke), Background = UI.Brush(Model.Fill), Padding = new Thickness(Model.Padding), IsUndoEnabled = false, AcceptsTab = true, VerticalScrollBarVisibility = owner.ControlsVisible ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            System.Windows.Automation.AutomationProperties.SetName(Editor, "메모 글상자 편집");
            SpellCheck.SetIsEnabled(Editor, false); body.Children.Add(Editor); owner.ActiveText = this;
            Editor.Loaded += delegate { if (Editor != null) { Editor.Focus(); Keyboard.Focus(Editor); FocusManager.SetFocusedElement(owner, Editor); } };
            Editor.TextChanged += delegate { Capture(); owner.MarkDirty(); };
            // Paste text as text; image paste becomes a separate movable canvas image.
            System.Windows.DataObject.AddPastingHandler(Editor, delegate(object sender, DataObjectPastingEventArgs e) {
                e.CancelCommand();
                try { if (Clipboard.ContainsImage()) owner.PasteImage(); else if (Clipboard.ContainsText()) Editor.Selection.Text = Clipboard.GetText(); }
                catch (Exception ex) { MemoApp.Error(ex); }
            });
            Editor.CommandBindings.Add(new CommandBinding(EditingCommands.ToggleBullets, delegate(object s, ExecutedRoutedEventArgs e) { e.Handled = true; }));
            Editor.CommandBindings.Add(new CommandBinding(EditingCommands.ToggleNumbering, delegate(object s, ExecutedRoutedEventArgs e) { e.Handled = true; }));
            Editor.Focus(); Editor.CaretPosition = Editor.Document.ContentEnd;
            owner.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(delegate { if (Editor != null) { Editor.Focus(); Keyboard.Focus(Editor); } }));
        }
        public void Capture() { if (Editor != null) Model.Paragraphs = TextCodec.Capture(Editor.Document); }
        public void StopEdit() { Capture(); RefreshBody(); }
    }
    sealed class ElementDrawing : FrameworkElement
    {
        readonly Element model;
        public ElementDrawing(Element model) { this.model = model; IsHitTestVisible = false; }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc); double s = model.StrokeWidth;
            var pen = s > 0 ? new Pen(UI.Brush(model.Stroke), s) : null; var fill = UI.Brush(model.Fill);
            var rect = new Rect(s / 2, s / 2, Math.Max(0, ActualWidth - s), Math.Max(0, ActualHeight - s));
            if (model.Type == "stroke") { InkDrawing.ToStroke(model, ActualWidth, ActualHeight).Draw(dc); }
            else if (model.Type == "ellipse") dc.DrawEllipse(fill, pen, new Point(ActualWidth / 2, ActualHeight / 2), rect.Width / 2, rect.Height / 2);
            else if (model.Type == "rounded") dc.DrawRoundedRectangle(fill, pen, rect, 12, 12);
            else if (model.Type == "line" || model.Type == "arrow")
            {
                Point p1, p2;
                if (model.LineOrientation == "horizontal") { p1 = new Point(5, ActualHeight / 2); p2 = new Point(ActualWidth - 5, ActualHeight / 2); }
                else if (model.LineOrientation == "vertical") { p1 = new Point(ActualWidth / 2, 5); p2 = new Point(ActualWidth / 2, ActualHeight - 5); }
                else if (model.LineOrientation == "up") { p1 = new Point(5, ActualHeight - 5); p2 = new Point(ActualWidth - 5, 5); }
                else { p1 = new Point(5, 5); p2 = new Point(ActualWidth - 5, ActualHeight - 5); }
                if (model.ReverseLine) { var swap = p1; p1 = p2; p2 = swap; }
                var linePen = new Pen(UI.Brush(model.Stroke), Math.Max(1, s)); dc.DrawLine(linePen, p1, p2);
                if (model.Type == "arrow")
                {
                    Vector direction = p1 - p2; direction.Normalize(); Vector normal = new Vector(-direction.Y, direction.X);
                    var geo = new StreamGeometry(); using (var c = geo.Open()) { c.BeginFigure(p2, true, true); c.LineTo(p2 + direction * 14 + normal * 6, true, false); c.LineTo(p2 + direction * 14 - normal * 6, true, false); } geo.Freeze(); dc.DrawGeometry(UI.Brush(model.Stroke), null, geo);
                }
            }
            else dc.DrawRectangle(fill, pen, rect);
        }
    }
}
