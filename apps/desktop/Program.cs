using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace StickyCanvas
{
    public static class Program
    {
        [STAThread] public static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--self-test") return SelfTests.Run(args[1]);
            string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StickyCanvas");
            int fixture = 0, lifetime = 0, hideAfter = 0; bool uiTest = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--profile" && i + 1 < args.Length) profile = args[++i];
                else if (args[i] == "--fixture" && i + 1 < args.Length) int.TryParse(args[++i], out fixture);
                else if (args[i] == "--ui-test") uiTest = true;
                else if (args[i] == "--lifetime" && i + 1 < args.Length) int.TryParse(args[++i], out lifetime);
                else if (args[i] == "--hide-after" && i + 1 < args.Length) int.TryParse(args[++i], out hideAfter);
            }
            bool owner;
            string hash; using (var s = new MemoryStream(Encoding.UTF8.GetBytes(Path.GetFullPath(profile).ToUpperInvariant()))) hash = Store.Sha(s).Substring(0, 24);
            using (var mutex = new Mutex(true, "Local\\StickyCanvas-" + hash, out owner))
            using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\StickyCanvas-Open-" + hash))
            {
                if (!owner) { signal.Set(); return 0; }
                try
                {
                    var app = new MemoApp(profile, fixture, uiTest, lifetime, hideAfter);
                    var registration = ThreadPool.RegisterWaitForSingleObject(signal, delegate { app.Dispatcher.BeginInvoke(new Action(app.ShowLibrary)); }, null, Timeout.Infinite, false);
                    try { app.Run(); }
                    finally { registration.Unregister(null); app.Dispose(); }
                    return 0;
                }
                catch (Exception ex) { MessageBox.Show("캔버스 메모를 시작하지 못했습니다.\n원본 데이터는 삭제하지 않았습니다.\n\n" + ex.Message, "캔버스 메모", MessageBoxButton.OK, MessageBoxImage.Error); return 1; }
            }
        }
    }
    public sealed class MemoApp : Application, IDisposable
    {
        public readonly Store Store;
        public readonly List<Note> Notes;
        public readonly Dictionary<string, NoteWindow> OpenWindows = new Dictionary<string, NoteWindow>();
        Forms.NotifyIcon tray; Drawing.Icon icon; LibraryWindow library;
        public bool Quitting { get; private set; }
        public readonly bool UiTestMode;
        int fileOperations;
        string recoveryDay; bool recovering;
        EventHandler displayChanged;
        public MemoApp(string profile, int fixture, bool uiTest = false, int lifetime = 0, int hideAfter = 0)
        {
            UiTestMode = uiTest;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Store = new Store(profile); Notes = Store.Load();
            if (fixture > 0 && Notes.Count == 0) for (int i = 0; i < Math.Min(10, fixture); i++)
            {
                var n = Note.Create(); n.Title = "시험 메모 " + (i + 1); n.Elements[0] = Element.Text("한글 메모 테스트\n창과 글상자를 움직여 보세요.");
                n.Elements.Add(new Element { Type = "ellipse", X = 340, Y = 30, Width = 100, Height = 90, Fill = "#FFA7D8B8", StrokeWidth = 2 });
                Notes.Add(n); Store.Save(n, new NoteView { Left = 100 + i * 32, Top = 90 + i * 32 });
            }
            Startup += delegate {
                BuildTray();
                displayChanged = delegate { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(delegate { foreach (var w in OpenWindows.Values.ToList()) UI.Place(w, w.Left, w.Top, w.Width, w.Height); })); };
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += displayChanged;
                if (Notes.Count == 0 && Store.ReadErrors.Count == 0) NewNote();
                else foreach (var n in Notes.Where(n => n.DeletedAt == null)) if (Store.View(n.Id).IsOpen) Open(n, false);
                if (Store.ReadErrors.Count > 0) ShowLibrary();
                // Daily recovery snapshot is a one-shot background task, not an idle polling loop.
                MaybeRecovery();
                if (fixture > 0)
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate {
                        bool hidden = OpenWindows.Values.All(w => !w.ShowInTaskbar);
                        bool tool = OpenWindows.Values.All(w => { long style = Native.GetWindowLong(new WindowInteropHelper(w).Handle, -20).ToInt64(); return (style & 0x80) != 0 && (style & 0x40000) == 0; });
                        File.WriteAllText(Path.Combine(Store.Root, "benchmark-window-styles.txt"), "taskbarHidden=" + hidden + ";toolWindows=" + tool + ";explicitShutdown=" + (ShutdownMode == ShutdownMode.OnExplicitShutdown));
                        File.WriteAllText(Path.Combine(Store.Root, "benchmark-ready.txt"), OpenWindows.Count.ToString());
                    }));
                    if (hideAfter > 0)
                    {
                        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(hideAfter) };
                        timer.Tick += delegate { timer.Stop(); foreach (var w in OpenWindows.Values.ToList()) w.Close(); File.WriteAllText(Path.Combine(Store.Root, "benchmark-hidden.txt"), OpenWindows.Count.ToString()); }; timer.Start();
                    }
                    if (lifetime > 0)
                    {
                        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(lifetime) }; timer.Tick += delegate { timer.Stop(); Quit(); }; timer.Start();
                    }
                }
            };
            SessionEnding += delegate(object sender, SessionEndingCancelEventArgs e) { if (!FlushAll()) e.Cancel = true; };
        }
        void BuildTray()
        {
            icon = AppIcon.Load(32);
            tray = new Forms.NotifyIcon { Icon = icon, Text = "캔버스 메모 · 우클릭 메뉴", Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("새 메모", null, delegate { NewNote(); });
            menu.Items.Add("메모 목록 / 휴지통", null, delegate { ShowLibrary(); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("모든 메모 보이기", null, delegate { foreach (var n in Notes.Where(n => n.DeletedAt == null).ToList()) Open(n, false); });
            menu.Items.Add("모든 메모 숨기기", null, delegate { foreach (var w in OpenWindows.Values.ToList()) w.Close(); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("백업 내보내기…", null, delegate { Export(); });
            menu.Items.Add("백업 가져오기…", null, delegate { Import(); });
            menu.Items.Add("데이터 폴더 열기", null, delegate { Process.Start("explorer.exe", "\"" + Store.Root + "\""); });
            var auto = new Forms.ToolStripMenuItem("Windows 로그인 시 실행") { Checked = AutoStartEnabled() };
            menu.Opening += delegate { auto.Checked = AutoStartEnabled(); };
            auto.Click += delegate { try { SetAutoStart(!auto.Checked); auto.Checked = AutoStartEnabled(); } catch (Exception ex) { Error(ex); } };
            menu.Items.Add(auto);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("캔버스 메모 종료", null, delegate { Quit(); });
            tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { ShowLibrary(); };
        }
        public void NewNote()
        {
            var n = Note.Create(); var v = new NoteView { Left = 90 + OpenWindows.Count * 24, Top = 90 + OpenWindows.Count * 24 };
            try { Store.Save(n, v); Notes.Add(n); Open(n, true); RefreshLibrary(); } catch (Exception ex) { Error(ex); }
        }
        public void Open(Note n, bool edit)
        {
            if (n.DeletedAt != null) return;
            NoteWindow existing;
            if (OpenWindows.TryGetValue(n.Id, out existing)) { existing.Show(); existing.Activate(); return; }
            var view = Store.View(n.Id); view.IsOpen = true;
            var w = new NoteWindow(this, n, view); OpenWindows[n.Id] = w;
            w.Closed += delegate { OpenWindows.Remove(n.Id); if (MainWindow == w) MainWindow = null; };
            w.Show();
            if (edit) w.BeginDefaultEditing();
        }
        public void Replace(Note note)
        {
            int index = Notes.FindIndex(n => n.Id == note.Id); if (index >= 0) Notes[index] = note;
        }
        public void ShowLibrary()
        {
            if (Quitting) return;
            if (library == null) { library = new LibraryWindow(this); library.Closed += delegate { if (MainWindow == library) MainWindow = null; library = null; }; }
            library.Refresh(); library.Show(); library.Activate();
        }
        public void RefreshLibrary() { if (library != null) library.Refresh(); }
        public void Trash(Note note)
        {
            note = Notes.First(n => n.Id == note.Id);
            NoteWindow w; if (OpenWindows.TryGetValue(note.Id, out w) && !w.Flush(true)) return;
            string old = note.DeletedAt; note.DeletedAt = DateTime.UtcNow.ToString("o");
            try
            {
                var v = Store.View(note.Id); v.IsOpen = false; Store.Save(note, v);
                if (w != null) { w.CloseAfterTrash(); } RefreshLibrary();
            }
            catch (Exception ex) { note.DeletedAt = old; Error(ex); }
        }
        public void Restore(Note n)
        {
            string old = n.DeletedAt; n.DeletedAt = null;
            try { Store.Save(n, new NoteView { IsOpen = false }); RefreshLibrary(); } catch (Exception ex) { n.DeletedAt = old; Error(ex); }
        }
        public bool FlushAll()
        {
            foreach (var w in OpenWindows.Values.ToList()) if (!w.Flush(true)) return false;
            return true;
        }
        public async void Export()
        {
            if (fileOperations > 0) { MessageBox.Show("백업 또는 가져오기가 진행 중입니다. 완료 후 다시 시도하세요.", "캔버스 메모"); return; }
            bool saved = true;
            foreach (var w in OpenWindows.Values.ToList()) saved &= w.Flush(false);
            var snapshot = Notes.Select(n => Json.Copy(n)).ToList();
            var dlg = new Microsoft.Win32.SaveFileDialog { Title = "메모와 이미지를 함께 백업", Filter = "캔버스 메모 백업 (*.zip)|*.zip", FileName = "canvas-memo-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".zip" };
            if (dlg.ShowDialog() != true) return;
            fileOperations++;
            try { await System.Threading.Tasks.Task.Run(delegate { Store.Export(dlg.FileName, snapshot); }); MessageBox.Show("백업을 저장했습니다." + (saved ? "" : "\nDB에 저장하지 못한 현재 편집본도 포함했습니다.") + "\n" + dlg.FileName, "캔버스 메모"); }
            catch (Exception ex) { Error(ex); }
            finally { fileOperations--; }
        }
        public async void Import()
        {
            if (fileOperations > 0) { MessageBox.Show("백업 또는 가져오기가 진행 중입니다. 완료 후 다시 시도하세요.", "캔버스 메모"); return; }
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "백업 가져오기 — 기존 메모를 덮어쓰지 않고 사본 추가", Filter = "캔버스 메모 백업 (*.zip)|*.zip" };
            if (dlg.ShowDialog() != true) return;
            fileOperations++;
            try { var copies = await System.Threading.Tasks.Task.Run(delegate { return Store.Import(dlg.FileName); }); Notes.AddRange(copies); ShowLibrary(); MessageBox.Show(copies.Count + "개 메모를 사본으로 가져왔습니다.", "캔버스 메모"); }
            catch (Exception ex) { Error(ex); }
            finally { fileOperations--; }
        }
        public void Quit()
        {
            if (fileOperations > 0) { MessageBox.Show("백업 또는 가져오기가 진행 중입니다. 완료 후 종료해 주세요.", "캔버스 메모"); return; }
            if (!FlushAll()) return;
            Quitting = true; tray.Visible = false; Shutdown();
        }
        public void MaybeRecovery()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (recovering || recoveryDay == today || Quitting) return;
            recovering = true;
            System.Threading.Tasks.Task.Run(delegate {
                bool success = false; try { Store.RecoverySnapshot(); success = true; } catch { }
                if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(delegate {
                    recovering = false; if (success) recoveryDay = today;
                    else if (!Quitting && tray != null) { recoveryDay = today; tray.Text = "캔버스 메모 · 자동 복구본 실패 (수동 백업 권장)"; }
                }));
            });
        }
        public static void Error(Exception ex) { MessageBox.Show(ex.Message, "캔버스 메모 · 확인이 필요합니다", MessageBoxButton.OK, MessageBoxImage.Warning); }
        bool AutoStartEnabled()
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")) return key != null && key.GetValue("StickyCanvas") != null;
        }
        public bool IsAutoStartEnabled { get { return AutoStartEnabled(); } }
        public void ToggleAutoStart()
        {
            try { bool enable = !AutoStartEnabled(); SetAutoStart(enable); MessageBox.Show(enable ? "Windows 로그인 시 자동 실행을 켰습니다." : "Windows 로그인 시 자동 실행을 껐습니다.", "캔버스 메모"); }
            catch (Exception ex) { Error(ex); }
        }
        void SetAutoStart(bool enabled)
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
            {
                if (enabled) key.SetValue("StickyCanvas", StartupCommand.Build(Process.GetCurrentProcess().MainModule.FileName));
                else key.DeleteValue("StickyCanvas", false);
            }
        }
        public void Dispose() { if (displayChanged != null) Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= displayChanged; if (tray != null) tray.Dispose(); if (icon != null) icon.Dispose(); Store.Dispose(); }
    }
    public static class StartupCommand
    {
        public static string Build(string executable)
        {
            string path = Path.GetFullPath(executable);
            string parent = Path.GetDirectoryName(Path.GetDirectoryName(path));
            string stable = Path.Combine(parent, "current", "StickyCanvas.exe");
            if (File.Exists(stable)) path = stable;
            string command = "\"" + path + "\"";
            if (path.Contains("\"") || command.Length > 260) throw new InvalidOperationException("자동 실행 경로가 올바르지 않습니다.");
            return command;
        }
    }
    public static class AppIcon
    {
        public static Drawing.Icon Load(int size)
        {
            using (var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("StickyCanvas.AppIcon.ico"))
            using (var source = new Drawing.Icon(stream, new Drawing.Size(size, size))) return (Drawing.Icon)source.Clone();
        }
        static ImageSource cached;
        public static ImageSource Source
        {
            get
            {
                if (cached == null) using (var icon = Load(32))
                {
                    var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                    bitmap.Freeze(); cached = bitmap;
                }
                return cached;
            }
        }
    }
    public static class Native
    {
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLong(IntPtr hwnd, int index, IntPtr value);
        public static void ToolWindow(Window w)
        {
            var app = Application.Current as MemoApp;
            if (app != null && app.UiTestMode) { w.ShowInTaskbar = true; return; }
            w.ShowInTaskbar = false;
            w.SourceInitialized += delegate { var hwnd = new WindowInteropHelper(w).Handle; long style = GetWindowLong(hwnd, -20).ToInt64(); SetWindowLong(hwnd, -20, new IntPtr((style | 0x80) & ~0x40000)); };
        }
    }
    public static class UI
    {
        public static readonly Brush Ink = Brush("#FF3E392F"), Muted = Brush("#FF746C5C"), Line = Brush("#FFD4CAB0");
        public static Brush Brush(string color) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }
        public static Button Button(string text, Action action, string tip = null)
        {
            var b = new Button { Content = text, Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(2), Foreground = Ink, Background = Brushes.Transparent, BorderBrush = Line, BorderThickness = new Thickness(1), FontSize = 12, Focusable = false, ToolTip = tip ?? text, MinHeight = 28 };
            System.Windows.Automation.AutomationProperties.SetName(b, tip ?? text); b.Click += delegate { action(); }; return b;
        }
        public static void Chrome(Window w)
        {
            w.Icon = AppIcon.Source;
            Native.ToolWindow(w); w.FontFamily = new FontFamily("Malgun Gothic"); w.Foreground = Ink; w.Background = Brush("#FFFFFBF1");
            w.WindowStyle = WindowStyle.None; w.ResizeMode = ResizeMode.CanResize; w.MinWidth = 340; w.MinHeight = 260;
            System.Windows.Shell.WindowChrome.SetWindowChrome(w, new System.Windows.Shell.WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
        }
        public static void Place(Window w, double left, double top, double width, double height)
        {
            // Screen coordinates are native pixels; convert to DIPs using the initial WPF DPI transform.
            double scale = 1;
            using (var g = Drawing.Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96.0;
            var match = Forms.Screen.AllScreens.FirstOrDefault(s => left < s.WorkingArea.Right / scale && left + 80 > s.WorkingArea.Left / scale && top >= s.WorkingArea.Top / scale - 5 && top < (s.WorkingArea.Bottom - 40) / scale);
            var area = (match ?? Forms.Screen.PrimaryScreen).WorkingArea;
            w.Width = Math.Max(w.MinWidth, Math.Min(width, area.Width / scale)); w.Height = Math.Max(w.MinHeight, Math.Min(height, area.Height / scale));
            w.Left = Math.Max(area.Left / scale, Math.Min(left, area.Right / scale - w.Width)); w.Top = Math.Max(area.Top / scale, Math.Min(top, area.Bottom / scale - 60));
        }
    }
}
