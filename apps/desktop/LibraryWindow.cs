using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StickyCanvas
{
    public sealed class LibraryWindow : Window
    {
        readonly MemoApp app; readonly TextBox search = new TextBox(); readonly ListBox list = new ListBox();
        readonly CheckBox trash = new CheckBox { Content = "휴지통 보기", Margin = new Thickness(10, 8, 10, 8) };
        readonly TextBlock count = new TextBlock { Foreground = UI.Muted, Margin = new Thickness(4, 6, 4, 6) };
        public LibraryWindow(MemoApp app)
        {
            this.app = app; Title = "캔버스 메모 · 목록"; UI.Chrome(this); Width = 650; Height = 530; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var root = new DockPanel { Margin = new Thickness(14) }; Content = new Border { BorderBrush = UI.Line, BorderThickness = new Thickness(1), Child = root };
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
            var close = UI.Button("×", Close, "목록 닫기 — 프로그램은 계속 실행됩니다"); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
            var title = new TextBlock { Text = "캔버스 메모", FontSize = 21, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }; header.Children.Add(title);
            title.MouseLeftButtonDown += delegate { if (System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };
            var footer = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            var actions = new WrapPanel(); footer.Children.Add(actions);
            actions.Children.Add(UI.Button("＋ 새 메모", app.NewNote));
            actions.Children.Add(UI.Button("열기 / 복구", delegate { var n = Selected(); if (n != null) { if (n.DeletedAt != null) app.Restore(n); else app.Open(n, false); } }));
            actions.Children.Add(UI.Button("휴지통으로", delegate { var n = Selected(); if (n != null && n.DeletedAt == null) app.Trash(n); }));
            actions.Children.Add(UI.Button("백업", app.Export)); actions.Children.Add(UI.Button("가져오기", app.Import));
            footer.Children.Add(count);
            footer.Children.Add(new TextBlock { Text = "창을 닫아도 계속 실행됩니다. 완전 종료는 트레이 아이콘 우클릭 → 종료", FontSize = 11, Foreground = UI.Muted, TextWrapping = TextWrapping.Wrap });
            var filters = new DockPanel { Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(filters, Dock.Top); root.Children.Add(filters);
            DockPanel.SetDock(trash, Dock.Right); filters.Children.Add(trash);
            search.Padding = new Thickness(8); search.ToolTip = "제목 또는 메모 내용 검색"; System.Windows.Automation.AutomationProperties.SetName(search, "메모 검색"); filters.Children.Add(search);
            list.BorderBrush = UI.Line; list.Background = Brushes.Transparent; list.HorizontalContentAlignment = HorizontalAlignment.Stretch; root.Children.Add(list);
            search.TextChanged += delegate { Refresh(); }; trash.Checked += delegate { Refresh(); }; trash.Unchecked += delegate { Refresh(); };
            list.MouseDoubleClick += delegate { var n = Selected(); if (n != null && n.DeletedAt == null) app.Open(n, false); };
        }
        Note Selected() { var item = list.SelectedItem as ListBoxItem; return item == null ? null : item.Tag as Note; }
        public void Refresh()
        {
            string selected = Selected() == null ? null : Selected().Id;
            list.Items.Clear(); string query = search.Text.Trim();
            foreach (var n in app.Notes.Where(n => (n.DeletedAt != null) == (trash.IsChecked == true) && n.SearchText.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).OrderByDescending(n => n.UpdatedAt))
            {
                var stack = new StackPanel { Margin = new Thickness(8) };
                stack.Children.Add(new TextBlock { Text = n.Title, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                string plain = string.Join(" ", n.Elements.Where(e => e.Type == "text").Select(e => e.PlainText.Replace('\n', ' ')));
                stack.Children.Add(new TextBlock { Text = plain.Length > 110 ? plain.Substring(0, 110) + "…" : plain, Foreground = UI.Muted, Margin = new Thickness(0, 4, 0, 3), TextTrimming = TextTrimming.CharacterEllipsis });
                DateTime dt; stack.Children.Add(new TextBlock { Text = DateTime.TryParse(n.UpdatedAt, out dt) ? dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "", FontSize = 11, Foreground = UI.Muted });
                var item = new ListBoxItem { Tag = n, Content = stack, Background = UI.Brush(n.Background), Margin = new Thickness(3) };
                list.Items.Add(item); if (n.Id == selected) list.SelectedItem = item;
            }
            count.Text = list.Items.Count + "개 메모" + (app.Store.ReadErrors.Count > 0 ? " · " + string.Join(" / ", app.Store.ReadErrors) : "");
        }
    }
}
