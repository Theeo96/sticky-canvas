using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StickyCanvas
{
    [DataContract] public class TestReport
    {
        [DataMember] public string Timestamp = DateTime.UtcNow.ToString("o");
        [DataMember] public List<string> Passed = new List<string>();
        [DataMember] public List<string> Failed = new List<string>();
        [DataMember] public string Scope = "Isolated synthetic data; no production notes, no physical IME or display-scale validation.";
    }
    public static class SelfTests
    {
        static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        static void Reject(Action action) { bool rejected = false; try { action(); } catch { rejected = true; } Assert(rejected, "Expected rejection"); }
        public static int Run(string reportPath)
        {
            var report = new TestReport();
            string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath)), "test-data", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            Action<string, Action> test = delegate(string name, Action body) { try { body(); report.Passed.Add(name); } catch (Exception ex) { report.Failed.Add(name + ": " + ex.GetType().Name + " — " + ex.Message); } };
            var note = Note.Create(); note.Title = "한글 ' ; DROP TABLE notes; -- 이모지 😀";
            note.Elements[0].Paragraphs = new List<TextParagraph> {
                new TextParagraph { Align = "Center", Runs = new List<TextRun> { new TextRun { Text = "제목", Size = 24, Bold = true, Color = "#FFAA2244" }, new TextRun { Text = " 한글😀", Italic = true, Underline = true } } },
                new TextParagraph { Align = "Right", LineHeight = 28, Runs = new List<TextRun> { new TextRun { Text = "두 번째 줄\n줄바꿈", Font = "Consolas", Size = 18 } } }
            };
            note.Elements.Add(new Element { Type = "rectangle", X = 330, Y = 20, Width = 100, Height = 80, Fill = "#FF11AA33", StrokeWidth = 3 });
            test("document JSON preserves Korean, emoji, inline styles, geometry and order", delegate { Rules.Validate(note); Assert(Json.Write(note) == Json.Write(Json.Read<Note>(Json.Write(note))), "JSON mismatch"); });
            test("unsupported schema, NaN, duplicate element and out-of-paper bounds rejected", delegate {
                var n = Json.Copy(note); n.SchemaVersion = 99; Reject(() => Rules.Validate(n)); n.SchemaVersion = 1; n.PaperWidth = double.NaN; Reject(() => Rules.Validate(n));
                n = Json.Copy(note); n.Elements.Add(Json.Copy(n.Elements[0])); Reject(() => Rules.Validate(n)); n = Json.Copy(note); n.Elements[0].X = 1000; Reject(() => Rules.Validate(n));
            });
            test("neutral text model converts to and from WPF without losing paragraph or run styles", delegate {
                var original = note.Elements[0]; var capture = TextCodec.Capture(TextCodec.Document(original));
                Assert(Json.Write(original.Paragraphs) == Json.Write(capture), "Text conversion mismatch");
            });
            test("undo/redo preserves earlier text and geometry; branching clears redo", delegate {
                var n = Json.Copy(note); var history = new History(n); n.Elements[0].X += 10; history.Record(n); n.Elements[0].Paragraphs[0].Runs[0].Text = "수정";
                var previous = history.Undo(n); Assert(previous.Elements[0].X == 30 && previous.Elements[0].PlainText.Contains("제목"), "text undo");
                var redo = history.Redo(); Assert(redo.Elements[0].PlainText.Contains("수정"), "text redo");
                var second = history.Undo(redo); second.Title = "branch"; history.Record(second); Assert(history.Redo() == null, "redo branch");
            });
            string pngPath = Path.Combine(root, "fixture.png");
            test("synthetic PNG fixture", delegate {
                var pixels = new byte[64 * 48 * 4]; for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 60; pixels[i + 1] = 160; pixels[i + 2] = 220; pixels[i + 3] = 255; }
                var bitmap = BitmapSource.Create(64, 48, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4); bitmap.Freeze();
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var s = File.Create(pngPath)) encoder.Save(s);
            });
            string profile = Path.Combine(root, "profile"); Asset asset = null; string backup = Path.Combine(root, "backup.zip");
            using (var store = new Store(profile))
            {
                test("SQLite parameter binding and transactional note/view save", delegate { store.Save(note, new NoteView()); var read = store.Load().Single(); Assert(read.Title == note.Title && Json.Write(read.Elements) == Json.Write(note.Elements), "DB roundtrip"); });
                test("SQLite rollback leaves both previous records intact", delegate {
                    using (var sql = new Sqlite(Path.Combine(profile, "notes.sqlite3")))
                    {
                        Reject(delegate { sql.Transaction(delegate { sql.Execute("UPDATE notes SET json=? WHERE id=?", "bad json", note.Id); sql.Execute("INSERT INTO missing_table VALUES(1)"); }); });
                    }
                    Assert(store.Load().Single().Title == note.Title, "Rollback mismatch");
                });
                test("image copied and still opens after original file is moved", delegate {
                    asset = store.ImportImage(pngPath); note.Elements.Add(new Element { Type = "image", AssetId = asset.Id, X = 340, Y = 160, Width = 64, Height = 48 }); store.Save(note, new NoteView());
                    File.Move(pngPath, pngPath + ".moved"); var bitmap = store.Bitmap(asset.Id); Assert(bitmap.PixelWidth == 64 && bitmap.PixelHeight == 48, "Image persistence");
                });
                test("bad image and unregistered asset reference rejected", delegate {
                    string bad = Path.Combine(root, "bad.png"); File.WriteAllText(bad, "not an image"); Reject(() => store.ImportImage(bad));
                    var n = Json.Copy(note); n.Elements.Last().AssetId = new string('a', 64); Reject(() => store.Save(n, new NoteView()));
                });
                test("trash and restore keep original element content, styles and asset references", delegate {
                    var original = Json.Write(note.Elements); note.DeletedAt = DateTime.UtcNow.ToString("o"); store.Save(note, new NoteView { IsOpen = false });
                    Assert(store.Load().Single().DeletedAt != null, "Trash"); note.DeletedAt = null; store.Save(note, new NoteView()); Assert(Json.Write(store.Load().Single().Elements) == original, "Restore");
                });
                test("portable ZIP backup includes original images and verified manifest", delegate { store.Export(backup); using (var z = ZipFile.OpenRead(backup)) Assert(z.GetEntry("manifest.json") != null && z.GetEntry("assets/" + asset.FileName) != null, "Backup entries"); });
                test("emergency backup can preserve a valid unsaved in-memory snapshot", delegate {
                    var live = Json.Copy(note); live.Title = "unsaved emergency copy"; string path = Path.Combine(root, "emergency.zip"); store.Export(path, new List<Note> { live });
                    using (var z = ZipFile.OpenRead(path)) using (var r = new StreamReader(z.GetEntry("manifest.json").Open())) Assert(Json.Read<BackupManifest>(r.ReadToEnd()).Notes[0].Title == live.Title, "Emergency backup lost current edit");
                    Assert(store.Load().First(n => n.Id == note.Id).Title == note.Title, "Emergency export changed committed data");
                });
                test("backup import creates new IDs and preserves styled text, shape and image", delegate {
                    var copies = store.Import(backup); Assert(copies.Count == 1 && copies[0].Id != note.Id && copies[0].Elements[0].Id != note.Elements[0].Id, "New IDs");
                    Assert(copies[0].Elements[0].PlainText == note.Elements[0].PlainText && copies[0].Elements.Last().AssetId == asset.Id, "Content import"); Assert(store.Load().Count == 2, "No overwrite");
                });
                test("unsafe ZIP paths rejected before any DB change", delegate {
                    string path = Path.Combine(root, "unsafe.zip"); using (var z = ZipFile.Open(path, ZipArchiveMode.Create)) using (var s = new StreamWriter(z.CreateEntry("../escape.txt").Open())) s.Write("unsafe");
                    int count = store.Load().Count; Reject(() => store.Import(path)); Assert(store.Load().Count == count && !File.Exists(Path.Combine(root, "escape.txt")), "Unsafe import changed destination");
                });
                test("image checksum mismatch in backup rejected before any DB change", delegate {
                    string path = Path.Combine(root, "corrupt.zip"); File.Copy(backup, path);
                    using (var z = ZipFile.Open(path, ZipArchiveMode.Update)) { string name = "assets/" + asset.FileName; z.GetEntry(name).Delete(); using (var s = new StreamWriter(z.CreateEntry(name).Open())) s.Write("broken"); }
                    int count = store.Load().Count; Reject(() => store.Import(path)); Assert(store.Load().Count == count, "Corrupt import changed destination");
                });
                test("SQLite online recovery snapshot is consistent and idempotent", delegate {
                    store.RecoverySnapshot(); store.RecoverySnapshot(); var files = Directory.GetFiles(Path.Combine(profile, "backups"), "*.sqlite3"); Assert(files.Length == 1, "Backup duplicates");
                    using (var sql = new Sqlite(files[0])) { Assert(sql.Query("PRAGMA quick_check", 1)[0][0] == "ok", "Integrity"); Assert(sql.Query("SELECT id FROM notes", 1).Count == 2, "Snapshot count"); }
                });
            }
            test("Store close/reopen preserves committed notes and image", delegate { using (var store = new Store(profile)) { var all = store.Load(); Assert(all.Count == 2 && all.Any(n => n.Id == note.Id), "Reopen"); Assert(store.Bitmap(asset.Id).PixelWidth == 64, "Image reopen"); } });
            test("save error does not claim success or overwrite previously saved data", delegate {
                using (var store = new Store(profile)) using (var blocker = new Sqlite(Path.Combine(profile, "notes.sqlite3")))
                {
                    blocker.Execute("BEGIN IMMEDIATE"); var changed = Json.Copy(note); changed.Title = "must not persist";
                    Reject(() => store.Save(changed, new NoteView())); blocker.Execute("ROLLBACK"); Assert(store.Load().First(n => n.Id == note.Id).Title == note.Title, "Failed write persisted");
                }
            });
            test("unknown document version is isolated and not overwritten", delegate {
                using (var sql = new Sqlite(Path.Combine(profile, "notes.sqlite3"))) { var n = Json.Copy(note); n.Id = Guid.NewGuid().ToString("N"); n.SchemaVersion = 99; sql.Execute("INSERT INTO notes(id,json) VALUES(?,?)", n.Id, Json.Write(n)); }
                using (var store = new Store(profile)) { Assert(store.Load().Count == 2 && store.ReadErrors.Count == 1, "Unknown schema handling"); Reject(() => store.Export(Path.Combine(root, "partial.zip"))); }
            });
            test("native window is excluded from taskbar and Alt-Tab; explicit-shutdown lifetime", delegate {
                using (var app = new MemoApp(Path.Combine(root, "window-profile"), 0))
                {
                    var n = Note.Create(); app.Notes.Add(n); app.Store.Save(n, new NoteView()); var window = new NoteWindow(app, n, new NoteView());
                    window.ShowActivated = false; window.Show(); window.UpdateLayout();
                    var hwnd = new WindowInteropHelper(window).EnsureHandle(); long style = Native.GetWindowLong(hwnd, -20).ToInt64();
                    Assert(!window.ShowInTaskbar && (style & 0x80) != 0 && (style & 0x40000) == 0, "Taskbar/AltTab style");
                    Assert(app.ShutdownMode == ShutdownMode.OnExplicitShutdown, "Lifetime");
                    test("native editor combines 3 text boxes, 3 shapes and 2 independently movable images", delegate {
                        window.InsertText("native editor test"); window.Duplicate();
                        window.Edit(delegate {
                            foreach (string kind in new[] { "rectangle", "ellipse", "arrow" }) window.Model.Elements.Add(new Element { Type = kind, X = 320, Y = 220, Width = 100, Height = 70, StrokeWidth = 2 });
                        });
                        string editorImage = Path.Combine(root, "editor-fixture.png"); File.Copy(pngPath + ".moved", editorImage);
                        window.ImportImage(editorImage); window.ImportImage(editorImage);
                        Assert(window.Model.Elements.Count == 8 && window.Model.Elements.Count(e => e.Type == "text") == 3 && window.Model.Elements.Count(e => e.Type == "image") == 2, "Mixed canvas");
                        Assert(window.Model.Elements.Select(e => e.Id).Distinct().Count() == 8, "Element identity"); Rules.Validate(window.Model);
                    });
                    test("native element delete/undo/redo preserves asset identity and z order", delegate {
                        string id = window.Selected.Model.Id, assetId = window.Selected.Model.AssetId;
                        window.Delete(); Assert(window.Model.Elements.Count == 7, "Delete"); window.Undo(); Assert(window.Model.Elements.Count == 8 && window.Model.Elements.Last().Id == id && window.Model.Elements.Last().AssetId == assetId, "Undo delete");
                        window.Redo(); Assert(window.Model.Elements.Count == 7, "Redo delete"); window.Undo();
                    });
                    test("native window resize leaves document coordinates and text sizes unchanged", delegate {
                        string before = Json.Write(window.Model.Elements); window.Width += 100; window.Height += 100;
                        Assert(before == Json.Write(window.Model.Elements), "Window resize altered document");
                    });
                    test("native edit session updates neutral text without changing other elements", delegate {
                        var v = window.Paper.Children.OfType<ElementView>().First(a => a.Model.Type == "text");
                        var other = Json.Write(window.Model.Elements.Where(e => e.Id != v.Model.Id).ToList());
                        window.Select(v); v.BeginEdit(); v.Editor.Selection.Text = "실제 편집 모델 😀"; v.Capture();
                        Assert(v.Model.PlainText.Contains("실제 편집 모델 😀"), "Native text edit");
                        Assert(other == Json.Write(window.Model.Elements.Where(e => e.Id != v.Model.Id).ToList()), "Text edit changed layout/other elements"); window.EndEditing();
                    });
                    test("toggle overlay preserves physical paper position and fit scale", delegate {
                        window.SetInkTool("select"); window.EndEditing(); window.SetUiPinned(false);
                        window.View.Fit = true; window.Render(); window.UpdateLayout();
                        string original = Json.Write(window.Model);
                        var origin = window.Paper.TranslatePoint(new Point(0, 0), window);
                        var scale = window.Paper.LayoutTransform.Value;
                        for (int i = 0; i < 4; i++) {
                            window.SetUiPinned(i % 2 == 0); window.UpdateLayout();
                            Assert(window.ControlsVisible == (i % 2 == 0), "Toggle state");
                            Assert(window.Paper.TranslatePoint(new Point(0, 0), window) == origin && window.Paper.LayoutTransform.Value == scale, "Overlay changed physical paper geometry");
                        }
                        window.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
                        Assert(!window.ControlsVisible, "Hover revealed tools");
                        window.SetInkTool("pen"); Assert(!window.ControlsVisible, "Ink forced UI open"); window.SetInkTool("select");
                        Assert(original == Json.Write(window.Model), "Chrome changed document");
                    });
                    test("adaptive contrast and title editing preserve note content", delegate {
                        foreach (string color in new[] { "#FF000000", "#FFFFFFFF", "#FF202040", "#FFFFE58A", "#FF777777" }) {
                            var bg = (Color)ColorConverter.ConvertFromString(color); var fg = NoteWindow.ForegroundFor(bg);
                            double x = NoteWindow.Luminance(bg), y = NoteWindow.Luminance(fg);
                            Assert((Math.Max(x, y) + .05) / (Math.Min(x, y) + .05) >= 4.5, "Insufficient text contrast");
                            string content = Json.Write(window.Model.Elements);
                            window.Model.Background = color; window.Render(); window.UpdateLayout();
                            Assert(((SolidColorBrush)window.ChromeForeground).Color == fg, "Theme not applied");
                            Assert(Json.Write(window.Model.Elements) == content, "Theme recolored note text");
                        }
                        Assert(!window.TitleEditing, "Title starts in editing mode");
                        window.BeginTitleEdit(); Assert(window.TitleEditing, "Title edit did not start");
                        window.FinishTitleEdit(); Assert(!window.TitleEditing, "Title edit did not finish");
                    });
                    test("pixel pen, partial eraser, undo and backup preserve text and images", delegate {
                        string original = Json.Write(window.Model.Elements);
                        var points = new System.Windows.Input.StylusPointCollection(new[] { new System.Windows.Input.StylusPoint(50, 50), new System.Windows.Input.StylusPoint(80, 90), new System.Windows.Input.StylusPoint(130, 60) });
                        var stroke = new System.Windows.Ink.Stroke(points, new System.Windows.Ink.DrawingAttributes { Color = Colors.Blue, Width = 5, Height = 5 });
                        window.CommitInk(stroke);Assert(window.Model.SchemaVersion==4 && window.Model.Elements.Last().IsDrawing,"Pixel drawing not stored");Rules.Validate(window.Model);
                        var drawing=window.Model.Elements.Last();string before=drawing.AssetId;
                        Assert(!window.EraseInkAt(new Point(400,330)),"Eraser hit blank paper");
                        Assert(window.EraseInkAt(new Point(80,90)),"Partial erase missed line");
                        Assert(window.Model.Elements.Last().AssetId!=before && window.Model.Elements.Last().IsDrawing,"Partial erase removed object");
                        Assert(Json.Write(window.Model.Elements.Take(window.Model.Elements.Count-1).ToList())==original,"Eraser changed other elements");
                        window.Undo();Assert(window.Model.Elements.Last().AssetId==before,"Undo pixel erase");
                        window.Flush(false);string path=Path.Combine(root,"ink.zip");app.Store.Export(path);var copies=app.Store.Import(path);
                        Assert(copies.Single().Elements.Last().AssetId==before && copies.Single().Elements.Last().IsDrawing,"Pixel backup loss");
                        window.SetInkTool("pen"); Assert(window.InkTool == "pen" && !window.ControlsVisible, "Pen mode presentation"); window.SetInkTool("select");
                    });
                    test("hidden-UI save failure shows retry badge without forcing tools open", delegate {
                        window.EndEditing(); window.SetUiPinned(false); window.Flush(false);
                        using (var blocker = new Sqlite(Path.Combine(app.Store.Root, "notes.sqlite3")))
                        {
                            blocker.Execute("BEGIN IMMEDIATE"); window.MarkDirty(); Assert(!window.Flush(false) && window.SaveWarningVisible && !window.ControlsVisible, "Failure hidden from reader"); blocker.Execute("ROLLBACK");
                        }
                        Assert(window.Flush(false) && !window.ControlsVisible && !window.SaveWarningVisible, "Retry or reading-mode recovery failed");
                    });
                    test("top toolbar and disclosure indicator; object insertion exits ink modes", delegate {
                        Assert(window.ToolsAtTop, "Toolbar should be at top");
                        window.SetInkTool("pen"); window.InsertText("Synthetic object-mode regression"); Assert(window.InkTool == "select" && window.ActiveText != null, "Text tool kept ink mode");
                        window.SetInkTool("erase"); window.InsertShape("rectangle"); Assert(window.InkTool == "select" && window.Selected.Model.Type == "rectangle", "Shape kept eraser mode");
                        window.SetInkTool("pen"); window.ImportImage(Path.Combine(root, "editor-fixture.png")); Assert(window.InkTool == "select" && window.Selected.Model.Type == "image", "Image kept pen mode");
                        window.SetInkTool("erase"); window.ToggleFormats(); Assert(window.InkTool == "select" && window.FormatsVisible && window.FormatToggleLabel.Contains("▴"), "Expanded disclosure");
                        window.ToggleFormats(); Assert(!window.FormatsVisible && window.FormatToggleLabel.Contains("▾"), "Collapsed disclosure");
                    });
                    test("paper alignment is undoable; grid snapping clamps at document edges", delegate {
                        var view = window.Paper.Children.OfType<ElementView>().First(a => a.Model.Type == "text"); window.Select(view);
                        double originalX = view.Model.X; window.AlignElement("right"); Assert(window.Selected.Model.X == window.Model.PaperWidth - window.Selected.Model.Width, "Align right");
                        window.Undo(); Assert(window.Selected.Model.X == originalX, "Undo alignment");
                        window.AlignElement("center"); Assert(window.Selected.Model.X == (window.Model.PaperWidth - window.Selected.Model.Width) / 2, "Align center");
                        window.View.SnapGrid = true; Assert(window.SnapPosition(13, 100) == 16 && window.SnapPosition(101, 99) == 99 && window.SnapPosition(-3, 100) == 0, "Grid bounds");
                        window.View.SnapGrid = false; Assert(window.SnapPosition(13, 100) == 13, "Grid disabled");
                    });
                    test("Flow expansion and mode switching preserve document geometry", delegate {
                        string before=Json.Write(window.Model.Elements); double w=window.Model.PaperWidth;
                        window.ExpandArea(480,360); Assert(window.Model.PaperWidth==Math.Min(8192,w+480),"Expansion");
                        Assert(before==Json.Write(window.Model.Elements),"Expansion changed elements");
                        window.ToggleFlow();Assert(window.View.Flow && !window.View.Fit,"Flow mode");window.ZoomAt(1.1,new Point(120,160));
                        window.ToggleFlow();Assert(!window.View.Flow && before==Json.Write(window.Model.Elements),"Mode changed document");
                    });
                    test("multi-selection group drag preserves spacing and undoes in one step", delegate {
                        var pair=window.Paper.Children.OfType<ElementView>().Take(2).ToArray();
                        window.SelectMany(pair[0],false);window.SelectMany(pair[1],true);Assert(window.SelectionCount==2,"Shift selection");window.SetGroup(true);
                        var a=window.Model.Elements.First(e=>e.Id==pair[0].Model.Id);var b=window.Model.Elements.First(e=>e.Id==pair[1].Model.Id);
                        Assert(a.GroupId!=null && a.GroupId==b.GroupId,"Group identity");double delta=b.X-a.X,old=a.X;
                        window.Record();window.StartGroupDrag(a);window.MoveGroupDrag(a,10,10);window.Record();Assert(Math.Abs(b.X-a.X-delta)<.001,"Group spacing");
                        window.Undo();Assert(window.Model.Elements.First(e=>e.Id==a.Id).X==old,"Undo group drag");
                        window.SetGroup(false);Rules.Validate(window.Model);
                    });
                    test("non-destructive image crop opacity restore and schema-3 backup round trip", delegate {
                        var image=window.Model.Elements.First(e=>e.Type=="image");string asset=image.AssetId;
                        window.Edit(delegate{window.Model.SchemaVersion=Math.Max(3,window.Model.SchemaVersion);image.ImageOpacity=.4;image.CropX=.1;image.CropY=.1;image.CropWidth=.5;image.CropHeight=.5;});
                        var bitmap=window.ImageSource(image);Assert(bitmap.PixelWidth>0 && image.AssetId==asset,"Crop source");
                        string path=Path.Combine(root,"schema3.zip");window.Flush(false);app.Store.Export(path);var imported=app.Store.Import(path);
                        Assert(imported.Any(n=>n.Elements.Any(e=>e.ImageOpacity==.4 && e.CropWidth==.5)),"Crop backup loss");
                        var invalid=Json.Copy(window.Model);invalid.Elements.First(e=>e.Type=="image").CropWidth=2;bool rejected=false;try{Rules.Validate(invalid);}catch{rejected=true;}Assert(rejected,"Invalid crop accepted");
                        window.RestoreImage(image);Assert(!image.CropX.HasValue && !image.ImageOpacity.HasValue && image.AssetId==asset,"Restore changed original");window.Undo();Assert(window.Model.Elements.First(e=>e.Id==image.Id).ImageOpacity==.4,"Undo restore");
                    });
                    test("selection scale, locked transforms and equal gaps preserve undo",delegate {
                        window.SetUiPinned(true);window.SelectAllElements();window.SetGroup(false);
                        window.SelectAllElements();var primary=window.Model.Elements.First();window.Record();window.StartGroupDrag(primary);
                        double old=primary.Width;window.ResizeSelection(-10,-10,1,1);window.Record();Rules.Validate(window.Model);Assert(primary.Width<old,"Selection resize did not scale");
                        window.Undo();Assert(window.Model.Elements.First().Width==old,"Undo selection scale");
                        window.SelectAllElements();window.LockSelection(true);string locked=Json.Write(window.Model.Elements);window.StartGroupDrag(window.Model.Elements.First());window.MoveGroupDrag(window.Model.Elements.First(),30,30);window.ResizeSelection(20,20,1,1);window.Delete();window.Distribute(false);
                        Assert(locked==Json.Write(window.Model.Elements),"Locked elements changed");window.LockSelection(false);
                        window.Edit(delegate{window.Model.Elements.Clear();foreach(double x in new[]{20.0,130.0,280.0}){var e=Element.Text("spacing");e.X=x;e.Y=20;e.Width=30;e.Height=30;window.Model.Elements.Add(e);}});
                        window.SelectAllElements();window.Distribute(false);var ordered=window.Model.Elements.OrderBy(e=>e.X).ToArray();Assert(Math.Abs((ordered[1].X-ordered[0].X)-(ordered[2].X-ordered[1].X))<.001,"Unequal gaps");window.Undo();Assert(window.Model.Elements[1].X==130,"Undo spacing");
                    });
                    test("legacy vector eraser converts only touched pixels and lock blocks erasing",delegate {
                        var points=new System.Windows.Input.StylusPointCollection(new[]{new System.Windows.Input.StylusPoint(50,50),new System.Windows.Input.StylusPoint(150,50)});
                        var vector=InkDrawing.FromStroke(new System.Windows.Ink.Stroke(points),window.Model.PaperWidth,window.Model.PaperHeight);
                        window.Edit(delegate{window.Model.Elements.Add(vector);});Assert(window.EraseInkAt(new Point(80,50)),"Legacy erase missed");Assert(vector.IsDrawing && vector.Points==null,"Legacy conversion");
                        string asset=vector.AssetId;window.Edit(delegate{vector.Locked=true;});Assert(!window.EraseInkAt(new Point(130,50)) && vector.AssetId==asset,"Locked drawing erased");
                        var bitmap=window.App.Store.Bitmap(asset,8192);var data=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];var rgba=new System.Windows.Media.Imaging.FormatConvertedBitmap(bitmap,PixelFormats.Pbgra32,null,0);rgba.CopyPixels(data,bitmap.PixelWidth*4,0);
                        Assert(data.Where((v,i)=>i%4==3).Any(v=>v==0)&&data.Where((v,i)=>i%4==3).Any(v=>v>0),"Partial erase did not preserve visible pixels");
                    });
                    window.Close();
                    Assert(app.Store.View(n.Id).IsOpen == false, "Close should hide persisted note"); Assert(app.Store.Load().Any(a => a.Id == n.Id), "Close deleted note");
                }
            });
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))); File.WriteAllText(reportPath, Json.Write(report), new UTF8Encoding(false));
            return report.Failed.Count == 0 ? 0 : 1;
        }
    }
}
