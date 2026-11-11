using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace StickyCanvas
{
    public sealed partial class NoteWindow : Window
    {
        public readonly MemoApp App;
        public Note Model { get; private set; }
        public readonly NoteView View;
        public readonly Canvas Paper = new Canvas();
        public ElementView ActiveText;
        public ElementView Selected { get; private set; }
        readonly History history;
        readonly Dictionary<string, ElementView> views = new Dictionary<string, ElementView>();
        readonly TextBox title = new TextBox();
        readonly Button status, pin;
        readonly Button formatToggle;
        readonly TextBlock selectionInfo = new TextBlock { FontSize = 10, Foreground = UI.Muted, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(6, 0, 2, 0) };
        readonly ScrollViewer scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = UI.Brush("#FFEDE9DD") };
        readonly StackPanel tools = new StackPanel(); readonly WrapPanel formats = new WrapPanel { Visibility = Visibility.Collapsed };
        readonly DispatcherTimer saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        bool loading = true, dirty, contentDirty, trashed, composing;
        DateTime lastChange, firstChange;
        public NoteWindow(MemoApp app, Note model, NoteView view)
        {
            App = app; Model = model; View = view; history = new History(model);
            Title = "캔버스 메모 · " + model.Title; UI.Chrome(this); UI.Place(this, view.Left, view.Top, view.Width, view.Height); Topmost = view.AlwaysOnTop;
            var outer = new Grid();
            var dock = new DockPanel { LastChildFill = false };
            outer.Children.Add(scroll); outer.Children.Add(dock);
            Content = new Border { BorderBrush = UI.Line, BorderThickness = new Thickness(1), Child = outer };
            var header = new DockPanel { Background = UI.Brush("#FFF7E7A0"), LastChildFill = true, Height = 36 };
            DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
            var drag = new TextBlock { Text = "⠿", FontSize = 22, Width = 27, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.SizeAll, ToolTip = "메모 창 이동" };
            DockPanel.SetDock(drag, Dock.Left); header.Children.Add(drag);
            drag.MouseLeftButtonDown += delegate { EndEditing(); if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove(); };
            var close = UI.Button("×", Close, "이 메모 숨기기 — 트레이에서 계속 실행"); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
            var more = UI.Button("⋯", delegate { MoreMenu(); }, "메모 설정"); DockPanel.SetDock(more, Dock.Right); header.Children.Add(more);
            pin = UI.Button("고정", delegate { View.AlwaysOnTop = !View.AlwaysOnTop; Topmost = View.AlwaysOnTop; UpdatePin(); MarkDirty(false); }, "다른 창 위에 고정 / 해제"); DockPanel.SetDock(pin, Dock.Right); header.Children.Add(pin);
            title.Text = Model.Title; title.MaxLength = 500; title.BorderThickness = new Thickness(0); title.Background = Brushes.Transparent; title.FontSize = 14; title.Padding = new Thickness(2, 7, 2, 4); title.VerticalContentAlignment = VerticalAlignment.Center;
            System.Windows.Automation.AutomationProperties.SetName(title, "메모 제목"); header.Children.Add(title);
            title.TextChanged += delegate { if (!loading) { Model.Title = title.Text; Title = "캔버스 메모 · " + Model.Title; MarkDirty(); } };
            DockPanel.SetDock(tools, Dock.Top); dock.Children.Add(tools);
            var toolbar = new WrapPanel { Margin = new Thickness(3, 2, 3, 1) }; tools.Children.Add(toolbar);
            toolbar.Children.Add(UI.Button("＋ 글상자", delegate { InsertText(""); }));
            toolbar.Children.Add(UI.Button("도형 ▾", ShapesMenu));
            toolbar.Children.Add(UI.Button("이미지", ChooseImage));
            formatToggle = UI.Button("Aa 서식 ▾", ToggleFormats, "글꼴 서식 펼치기 / 접기");
            toolbar.Children.Add(formatToggle);
            toolbar.Children.Add(UI.Button("배치 ▾", PlacementMenu, "선택 요소 정렬 / 8칸 격자 스냅"));
            toolbar.Children.Add(UI.Button("↶", Undo, "실행 취소 (Ctrl+Z)")); toolbar.Children.Add(UI.Button("↷", Redo, "다시 실행 (Ctrl+Y)"));
            toolbar.Children.Add(UI.Button("목록", app.ShowLibrary));
            BuildInkTools(toolbar);
            tools.Children.Add(formats); BuildFormatBar();
            var footer = new DockPanel { Height = 29, Background = UI.Brush("#FFFFF9DF") }; DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);
            status = UI.Button("로컬 저장됨", delegate { Flush(true); }, "저장 상태 · 클릭해 즉시 저장/재시도"); status.FontSize = 10; status.Padding = new Thickness(4, 0, 4, 0); status.MinHeight = 20; status.BorderThickness = new Thickness(0); DockPanel.SetDock(status, Dock.Right); footer.Children.Add(status);
            var zoom = UI.Button(view.Fit ? "전체 맞춤" : "100%", delegate { View.Fit = !View.Fit; View.Zoom = 1; ApplyZoom(); MarkDirty(false); }, "100% / 전체 맞춤 전환");
            zoom.FontSize = 10; zoom.Padding = new Thickness(3, 0, 3, 0); zoom.MinHeight = 20; DockPanel.SetDock(zoom, Dock.Right); footer.Children.Add(zoom);
            selectionInfo.MouseLeftButtonDown += delegate { Properties(); }; footer.Children.Add(selectionInfo);
            scroll.Content = Paper;
            scroll.HorizontalContentAlignment = HorizontalAlignment.Left; scroll.VerticalContentAlignment = VerticalAlignment.Top;
            InitializePresentation(outer, dock, header);
            Paper.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { if (e.OriginalSource == Paper) { EndEditing(); Select(null); Paper.Focus(); } };
            Paper.Focusable = true; scroll.SizeChanged += delegate { ApplyZoom(); zoom.Content = View.Fit ? "전체 맞춤" : Math.Round(View.Zoom * 100) + "%"; };
            AllowDrop = true;
            DragOver += delegate(object s, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            Drop += delegate(object s, DragEventArgs e) { var files = e.Data.GetData(DataFormats.FileDrop) as string[]; if (files != null) foreach (var file in files.Take(10)) ImportImage(file); e.Handled = true; };
            saveTimer.Tick += delegate { if ((DateTime.UtcNow - lastChange).TotalMilliseconds >= 280 || (DateTime.UtcNow - firstChange).TotalMilliseconds >= 1000) Flush(false); };
            LocationChanged += delegate { if (!loading) MarkDirty(false); }; SizeChanged += delegate { if (!loading) MarkDirty(false); };
            Closing += delegate(object s, System.ComponentModel.CancelEventArgs e) {
                if (App.Quitting || trashed) return;
                if (!Flush(true)) { e.Cancel = true; return; }
                bool before = View.IsOpen; View.IsOpen = false;
                try { App.Store.Save(Model, View); } catch (Exception ex) { View.IsOpen = before; e.Cancel = true; MemoApp.Error(ex); }
            };
            Closed += delegate { saveTimer.Stop(); hideChromeTimer.Stop(); ActiveText = null; views.Clear(); Paper.Children.Clear(); };
            PreviewKeyDown += Keys;
            if (app.UiTestMode) PreviewMouseDown += delegate(object sender, MouseButtonEventArgs e) {
                var point = e.GetPosition(this); var hit = InputHitTest(point) as DependencyObject;
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate {
                    string details = "point=" + point + "; source=" + e.OriginalSource.GetType().Name + "; hit=" + (hit == null ? "null" : hit.GetType().Name) + "; focus=" + (Keyboard.FocusedElement == null ? "null" : Keyboard.FocusedElement.GetType().Name);
                    if (ActiveText != null && ActiveText.Editor != null) details += "; editor=" + ActiveText.Editor.ActualWidth + "x" + ActiveText.Editor.ActualHeight + "; loaded=" + ActiveText.Editor.IsLoaded + "; enabled=" + ActiveText.Editor.IsEnabled + "; focusable=" + ActiveText.Editor.Focusable;
                    File.WriteAllText(Path.Combine(App.Store.Root, "ui-diagnostics.txt"), details);
                }));
            };
            TextCompositionManager.AddPreviewTextInputStartHandler(this, delegate { composing = true; });
            TextCompositionManager.AddPreviewTextInputUpdateHandler(this, delegate { composing = true; });
            PreviewTextInput += delegate { composing = false; };
            Render(); UpdatePin(); loading = false;
            Loaded += delegate { MarkDirty(false); };
        }
        void UpdatePin() { pin.Content = View.AlwaysOnTop ? "고정됨" : "고정"; pin.FontWeight = View.AlwaysOnTop ? FontWeights.Bold : FontWeights.Normal; }
        void BuildFormatBar()
        {
            var font = new ComboBox { Width = 130, IsEditable = true, Margin = new Thickness(3), Text = "Malgun Gothic", ToolTip = "글꼴 이름" };
            foreach (string name in new[] { "Malgun Gothic", "Segoe UI", "Arial", "Consolas", "Gulim", "Batang" }) font.Items.Add(name);
            Action applyFont = delegate { string name = font.Text.Trim(); if (name.Length > 0 && name.Length <= 120) Format(TextElement.FontFamilyProperty, new FontFamily(name)); };
            font.DropDownClosed += delegate { applyFont(); }; font.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { applyFont(); e.Handled = true; } }; formats.Children.Add(font);
            var size = new ComboBox { Width = 62, IsEditable = true, Margin = new Thickness(3), Text = "16", ToolTip = "글자 크기 (8~144)" };
            foreach (int n in new[] { 10, 12, 14, 16, 18, 24, 32, 48, 72 }) size.Items.Add(n.ToString());
            Action applySize = delegate { double n; if (double.TryParse(size.Text, out n) && Rules.Number(n, 8, 144)) Format(TextElement.FontSizeProperty, n); };
            size.DropDownClosed += delegate { applySize(); }; size.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { applySize(); e.Handled = true; } }; formats.Children.Add(size);
            formats.Children.Add(UI.Button("B", delegate { Toggle(EditingCommands.ToggleBold); }, "굵게")); formats.Children.Add(UI.Button("I", delegate { Toggle(EditingCommands.ToggleItalic); }, "기울임")); formats.Children.Add(UI.Button("U", delegate { Toggle(EditingCommands.ToggleUnderline); }, "밑줄"));
            formats.Children.Add(UI.Button("글자색", delegate { PickColor("#FF29251F", c => Format(TextElement.ForegroundProperty, UI.Brush(c))); }));
            formats.Children.Add(UI.Button("왼쪽", delegate { Align(TextAlignment.Left); })); formats.Children.Add(UI.Button("가운데", delegate { Align(TextAlignment.Center); })); formats.Children.Add(UI.Button("오른쪽", delegate { Align(TextAlignment.Right); }));
            formats.Children.Add(UI.Button("요소 설정", Properties, "위치·크기·색·테두리·여백·줄간격"));
        }
        void Format(DependencyProperty property, object value)
        {
            var v = ActiveText ?? Selected; if (v == null || v.Model.Type != "text") return;
            if (v.Editor == null) v.BeginEdit();
            try
            {
                var selection = v.Editor.Selection;
                if (selection.IsEmpty && v.Model.PlainText.Length > 0) new TextRange(v.Editor.Document.ContentStart, v.Editor.Document.ContentEnd).ApplyPropertyValue(property, value);
                else selection.ApplyPropertyValue(property, value);
                v.Capture(); Record(); MarkDirty(); v.Editor.Focus();
            }
            catch (Exception ex) { MemoApp.Error(ex); }
        }
        void Toggle(RoutedUICommand command)
        {
            var v = ActiveText ?? Selected; if (v == null || v.Model.Type != "text") return;
            if (v.Editor == null) v.BeginEdit();
            if (v.Editor.Selection.IsEmpty && v.Model.PlainText.Length > 0) v.Editor.SelectAll();
            command.Execute(null, v.Editor); v.Capture(); Record(); MarkDirty(); v.Editor.Focus();
        }
        void Align(TextAlignment align)
        {
            var v = ActiveText ?? Selected; if (v == null || v.Model.Type != "text") return;
            if (v.Editor == null) v.BeginEdit();
            if (v.Editor.Selection.IsEmpty) foreach (var p in v.Editor.Document.Blocks.OfType<Paragraph>()) p.TextAlignment = align;
            else v.Editor.Selection.ApplyPropertyValue(Paragraph.TextAlignmentProperty, align);
            v.Capture(); Record(); MarkDirty(); v.Editor.Focus();
        }
        public void Render()
        {
            string selected = Selected == null ? null : Selected.Model.Id;
            ActiveText = null; Selected = null; Paper.Children.Clear(); views.Clear();
            Paper.Width = Model.PaperWidth; Paper.Height = Model.PaperHeight; Paper.Background = UI.Brush(Model.Background);
            foreach (var e in Model.Elements) { var v = new ElementView(this, e); views[e.Id] = v; Paper.Children.Add(v); if (e.Id == selected) Selected = v; }
            if (Selected != null) Selected.SetSelected(ControlsVisible);
            bool before = loading; loading = true; title.Text = Model.Title; loading = before;
            Title = "캔버스 메모 · " + Model.Title; scroll.Background = Paper.Background; Background = Paper.Background;
            ConfigureInkOverlay(); ApplyZoom(); UpdateSelectionInfo(); RefreshChrome();
        }
        void ApplyZoom()
        {
            double zoom = View.Fit ? Math.Min(Math.Max(50, scroll.ViewportWidth) / Model.PaperWidth, Math.Max(50, scroll.ViewportHeight) / Model.PaperHeight) : View.Zoom;
            zoom = Math.Max(.1, Math.Min(4, zoom)); Paper.LayoutTransform = new ScaleTransform(zoom, zoom);
        }
        public void Select(ElementView view)
        {
            if (Selected == view) return;
            if (ActiveText != null && ActiveText != view) EndEditing();
            if (Selected != null) Selected.SetSelected(false); Selected = view;
            if (Selected != null) Selected.SetSelected(ControlsVisible); UpdateSelectionInfo();
        }
        public void UpdateSelectionInfo()
        {
            selectionInfo.Text = Selected == null ? "더블클릭: 글 편집 · 손잡이: 이동" : string.Format(CultureInfo.CurrentCulture, "{0} · {1:0},{2:0} · {3:0}×{4:0}  ⚙", Selected.Model.Type == "text" ? "글상자" : Selected.Model.Type == "image" ? "이미지" : "도형", Selected.Model.X, Selected.Model.Y, Selected.Model.Width, Selected.Model.Height);
        }
        public void BeginDefaultEditing() { Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate { var first = Model.Elements.FirstOrDefault(e => e.Type == "text"); if (first != null) { Select(views[first.Id]); views[first.Id].BeginEdit(); } })); }
        public void EndEditing() { if (ActiveText != null) { var active = ActiveText; ActiveText = null; active.StopEdit(); Record(); } }
        public void Record() { history.Record(Model); }
        public void MarkDirty(bool content = true)
        {
            if (loading || App.Quitting || trashed) return;
            if (!dirty) firstChange = DateTime.UtcNow; lastChange = DateTime.UtcNow; dirty = true; contentDirty |= content;
            status.Content = "저장 중…"; status.Foreground = UI.Muted; saveTimer.Start();
        }
        public bool Flush(bool alert)
        {
            saveTimer.Stop(); if (trashed) return true;
            if (ActiveText != null) ActiveText.Capture();
            if (!dirty) return true;
            long rev = Model.Revision; string updated = Model.UpdatedAt;
            try
            {
                if (contentDirty) { Rules.Validate(Model); history.Record(Model); Model.Revision++; Model.UpdatedAt = DateTime.UtcNow.ToString("o"); }
                if (WindowState == WindowState.Normal) { View.Left = Left; View.Top = Top; View.Width = Width; View.Height = Height; }
                App.Store.Save(Model, View); dirty = false; contentDirty = false;
                status.Content = "로컬 저장됨"; status.Foreground = UI.Muted; saveFailed = false; RefreshChrome(); App.RefreshLibrary(); App.MaybeRecovery(); return true;
            }
            catch (Exception ex)
            {
                Model.Revision = rev; Model.UpdatedAt = updated; status.Content = "저장 실패 · 재시도"; status.Foreground = UI.Brush("#FFB23B28");
                saveFailed = true; RefreshChrome();
                if (alert) MemoApp.Error(ex); return false;
            }
        }
        public void CloseAfterTrash() { trashed = true; saveTimer.Stop(); Close(); }
        public void Edit(Action action)
        {
            EndEditing(); Record(); var before = Json.Copy(Model);
            try { action(); Rules.Validate(Model); Render(); Record(); MarkDirty(); }
            catch (Exception ex) { Model = before; App.Replace(Model); Render(); MemoApp.Error(ex); }
        }
        public void InsertText(string text)
        {
            ExitInkForObjectAction();
            var e = Element.Text(text); Add(e);
            if (views.ContainsKey(e.Id)) { Select(views[e.Id]); views[e.Id].BeginEdit(); }
        }
        void Add(Element e)
        {
            Edit(delegate {
                double offset = 20 + Model.Elements.Count % 7 * 18;
                e.Width = Math.Min(e.Width, Model.PaperWidth - 20); e.Height = Math.Min(e.Height, Model.PaperHeight - 20);
                e.X = Math.Min(offset, Model.PaperWidth - e.Width); e.Y = Math.Min(offset, Model.PaperHeight - e.Height); Model.Elements.Add(e);
            });
            if (views.ContainsKey(e.Id)) Select(views[e.Id]);
        }
        void ShapesMenu()
        {
            ExitInkForObjectAction();
            var menu = new ContextMenu();
            foreach (var item in new[] { new[] { "사각형", "rectangle" }, new[] { "둥근 사각형", "rounded" }, new[] { "타원", "ellipse" }, new[] { "선", "line" }, new[] { "화살표", "arrow" } })
            {
                string kind = item[1]; var m = new MenuItem { Header = item[0] };
                m.Click += delegate { InsertShape(kind); }; menu.Items.Add(m);
            }
            TrackMenu(menu); menu.IsOpen = true;
        }
        void ChooseImage()
        {
            ExitInkForObjectAction();
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.webp", Multiselect = true };
            if (d.ShowDialog(this) == true) foreach (string p in d.FileNames.Take(10)) ImportImage(p);
        }
        public void InsertShape(string kind)
        {
            ExitInkForObjectAction();
            Add(new Element { Type = kind, Width = 160, Height = kind == "line" || kind == "arrow" ? 60 : 100, Fill = kind == "line" || kind == "arrow" ? "#00FFFFFF" : "#FFA9D5C0", StrokeWidth = 2 });
        }
        public void ImportImage(string path)
        {
            ExitInkForObjectAction();
            try
            {
                var a = App.Store.ImportImage(path); double scale = Math.Min(1, Math.Min(280.0 / a.PixelWidth, 220.0 / a.PixelHeight));
                double w = a.PixelWidth * scale, h = a.PixelHeight * scale;
                if (Math.Min(w, h) < 16) { double grow = 16 / Math.Min(w, h); w *= grow; h *= grow; }
                if (w > Model.PaperWidth - 20 || h > Model.PaperHeight - 20) { double shrink = Math.Min((Model.PaperWidth - 20) / w, (Model.PaperHeight - 20) / h); w *= shrink; h *= shrink; }
                Rules.Require(w >= 16 && h >= 16, "현재 종이에 넣기 어려운 가로세로 비율입니다. 종이를 먼저 늘려 주세요.");
                Add(new Element { Type = "image", AssetId = a.Id, Width = w, Height = h });
            }
            catch (Exception ex) { MemoApp.Error(new IOException("이미지를 넣지 못했습니다. PNG/JPEG로 변환하거나 파일을 확인하세요.\n" + ex.Message)); }
        }
        public void PasteImage()
        {
            string temp = Path.Combine(Path.GetTempPath(), "sticky-canvas-" + Guid.NewGuid().ToString("N") + ".png");
            try { var source = Clipboard.GetImage(); if (source == null) return; var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(source)); using (var s = File.Create(temp)) png.Save(s); ImportImage(temp); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public void Duplicate()
        {
            if (Selected == null) return; var e = Json.Copy(Selected.Model); e.Id = Guid.NewGuid().ToString("N");
            Edit(delegate { e.X = Math.Min(Model.PaperWidth - e.Width, e.X + 16); e.Y = Math.Min(Model.PaperHeight - e.Height, e.Y + 16); Model.Elements.Add(e); });
            Select(views[e.Id]);
        }
        public void Delete() { if (Selected == null) return; string id = Selected.Model.Id; Edit(delegate { Model.Elements.RemoveAll(e => e.Id == id); }); }
        public void Reorder(int delta)
        {
            if (Selected == null) return; var e = Selected.Model;
            Edit(delegate { int index = Model.Elements.IndexOf(e); Model.Elements.Remove(e); Model.Elements.Insert(Math.Max(0, Math.Min(Model.Elements.Count, index + delta)), e); });
        }
        public void Undo()
        {
            EndEditing(); var previous = history.Undo(Model); if (previous == null) return;
            previous.Revision = Model.Revision; Model = previous; App.Replace(Model); Render(); MarkDirty();
        }
        public void Redo()
        {
            EndEditing(); var next = history.Redo(); if (next == null) return;
            next.Revision = Model.Revision; Model = next; App.Replace(Model); Render(); MarkDirty();
        }
        public void FitTextHeight()
        {
            if (Selected == null || Selected.Model.Type != "text") return; var e = Selected.Model;
            Edit(delegate { var t = TextCodec.Display(e); t.Measure(new Size(e.Width - e.StrokeWidth * 2, double.PositiveInfinity)); e.Height = Math.Min(Model.PaperHeight - e.Y, Math.Max(16, t.DesiredSize.Height + e.StrokeWidth * 2 + 4)); });
        }
        public void Properties()
        {
            if (Selected == null) return; EndEditing(); var element = Selected.Model; var copy = Json.Copy(element);
            if (PropertyDialog.Edit(this, copy)) Edit(delegate { int index = Model.Elements.FindIndex(e => e.Id == copy.Id); Model.Elements[index] = copy; });
        }
        void PickColor(string initial, Action<string> apply)
        {
            using (var d = new System.Windows.Forms.ColorDialog { FullOpen = true })
            {
                try { var c = (Color)ColorConverter.ConvertFromString(initial); d.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B); } catch { }
                if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) apply("#FF" + d.Color.R.ToString("X2") + d.Color.G.ToString("X2") + d.Color.B.ToString("X2"));
            }
        }
        void MoreMenu()
        {
            var menu = new ContextMenu();
            Action<string, Action> add = delegate(string label, Action action) { var m = new MenuItem { Header = label }; m.Click += delegate { action(); }; menu.Items.Add(m); };
            add("새 메모", App.NewNote); add("메모 목록", App.ShowLibrary);
            add("도구 고정 표시: " + (View.UiPinned ? "켜짐" : "꺼짐"), delegate { SetUiPinned(!View.UiPinned); });
            add("마우스 오버 표시: " + (View.HoverUi == "off" ? "꺼짐" : "켜짐"), delegate { View.HoverUi = View.HoverUi == "off" ? "on" : "off"; RefreshChrome(); MarkDirty(false); });
            add("Windows 로그인 시 자동 실행: " + (App.IsAutoStartEnabled ? "켜짐" : "꺼짐"), App.ToggleAutoStart);
            add("메모 배경색…", delegate { PickColor(Model.Background, c => Edit(delegate { Model.Background = c; })); });
            add("종이 크기 변경…", delegate { double w, h; if (PropertyDialog.PaperSize(this, Model.PaperWidth, Model.PaperHeight, out w, out h)) Edit(delegate { Model.PaperWidth = w; Model.PaperHeight = h; }); });
            add("100%로 보기", delegate { View.Fit = false; View.Zoom = 1; ApplyZoom(); MarkDirty(false); });
            add("종이 전체 맞춤", delegate { View.Fit = true; ApplyZoom(); MarkDirty(false); });
            var elements = new MenuItem { Header = "요소 선택 / 가려진 요소 찾기" };
            foreach (var e in Model.Elements.AsEnumerable().Reverse())
            {
                string id = e.Id; string text = e.Type == "text" ? e.PlainText.Replace('\n', ' ') : e.Type;
                var m = new MenuItem { Header = text.Length > 35 ? text.Substring(0, 35) + "…" : string.IsNullOrEmpty(text) ? "빈 글상자" : text };
                m.Click += delegate { EndEditing(); if (views.ContainsKey(id)) Select(views[id]); }; elements.Items.Add(m);
            }
            menu.Items.Add(elements);
            add("백업 내보내기…", App.Export); add("이 메모를 휴지통으로", delegate { App.Trash(Model); });
            TrackMenu(menu); menu.IsOpen = true;
        }
        void Keys(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0, shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (App.UiTestMode && ctrl && shift && e.Key == Key.Q) { e.Handled = true; App.Quit(); return; }
            if (App.UiTestMode) Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate {
                string details = "key=" + e.Key + "; focus=" + (Keyboard.FocusedElement == null ? "null" : Keyboard.FocusedElement.GetType().Name);
                if (ActiveText != null && ActiveText.Editor != null) details += "; editor=" + ActiveText.Editor.ActualWidth + "x" + ActiveText.Editor.ActualHeight + "; loaded=" + ActiveText.Editor.IsLoaded + "; visible=" + ActiveText.Editor.IsVisible + "; textLength=" + new TextRange(ActiveText.Editor.Document.ContentStart, ActiveText.Editor.Document.ContentEnd).Text.Length;
                File.WriteAllText(Path.Combine(App.Store.Root, "key-diagnostics.txt"), details);
            }));
            if (composing || e.Key == Key.ImeProcessed) return;
            bool ordinaryInput = Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox;
            if (ctrl && e.Key == Key.N) { App.NewNote(); e.Handled = true; }
            else if (ctrl && shift && e.Key == Key.M) { App.ShowLibrary(); e.Handled = true; }
            else if (ctrl && e.Key == Key.S) { Flush(true); e.Handled = true; }
            else if (ordinaryInput) return;
            else if (ctrl && e.Key == Key.Z) { if (shift) Redo(); else Undo(); e.Handled = true; }
            else if (ctrl && e.Key == Key.Y) { Redo(); e.Handled = true; }
            else if (e.Key == Key.Escape) { SetInkTool("select"); EndEditing(); Select(null); Paper.Focus(); e.Handled = true; }
            else if (ActiveText != null) return;
            else if (ctrl && e.Key == Key.V) { try { if (Clipboard.ContainsImage()) PasteImage(); else if (Clipboard.ContainsText()) InsertText(Clipboard.GetText()); } catch (Exception ex) { MemoApp.Error(ex); } e.Handled = true; }
            else if (ctrl && e.Key == Key.D) { Duplicate(); e.Handled = true; }
            else if (e.Key == Key.Delete || e.Key == Key.Back) { Delete(); e.Handled = true; }
            else if (e.Key == Key.Enter && Selected != null) { Selected.BeginEdit(); e.Handled = true; }
            else if (Selected != null && new[] { Key.Left, Key.Right, Key.Up, Key.Down }.Contains(e.Key))
            {
                var m = Selected.Model; double step = shift ? 10 : 1;
                Edit(delegate { m.X = Math.Max(0, Math.Min(Model.PaperWidth - m.Width, m.X + (e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0))); m.Y = Math.Max(0, Math.Min(Model.PaperHeight - m.Height, m.Y + (e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0))); }); e.Handled = true;
            }
        }
    }
}
