using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace StickyCanvas
{
    [DataContract] public class InkPoint
    {
        [DataMember] public double X, Y;
    }
    [DataContract] public class TextRun
    {
        [DataMember] public string Text = "";
        [DataMember] public string Font = "Malgun Gothic";
        [DataMember] public double Size = 16;
        [DataMember] public string Color = "#FF29251F";
        [DataMember] public bool Bold, Italic, Underline;
    }
    [DataContract] public class TextParagraph
    {
        [DataMember] public string Align = "Left";
        [DataMember] public double LineHeight = 0;
        [DataMember] public List<TextRun> Runs = new List<TextRun>();
    }
    [DataContract] public class Element
    {
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Type = "text";
        [DataMember] public double X = 20, Y = 20, Width = 300, Height = 140;
        [DataMember] public string Fill = "#00FFFFFF", Stroke = "#FF756744";
        [DataMember] public double StrokeWidth = 0, Padding = 6;
        [DataMember] public string AssetId = "";
        [DataMember(EmitDefaultValue = false)] public string GroupId;
        [DataMember(EmitDefaultValue = false)] public bool Locked, IsDrawing;
        [DataMember(EmitDefaultValue = false)] public double? ImageOpacity;
        [DataMember(EmitDefaultValue = false)] public double? CropX, CropY, CropWidth, CropHeight;
        [DataMember] public bool ReverseLine;
        [DataMember] public string LineOrientation = "horizontal";
        [DataMember(EmitDefaultValue = false)] public List<InkPoint> Points;
        [DataMember] public List<TextParagraph> Paragraphs = new List<TextParagraph>();
        public static Element Text(string text)
        {
            var e = new Element();
            e.Paragraphs.Add(new TextParagraph { Runs = new List<TextRun> { new TextRun { Text = text } } });
            return e;
        }
        public string PlainText { get { return string.Join("\n", Paragraphs.Select(p => string.Concat(p.Runs.Select(r => r.Text)))); } }
    }
    [DataContract] public class Note
    {
        [DataMember] public int SchemaVersion = 1;
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Title = "새 메모";
        [DataMember] public double PaperWidth = 480, PaperHeight = 360;
        [DataMember] public string Background = "#FFFFF1AB";
        [DataMember] public List<Element> Elements = new List<Element>();
        [DataMember] public long Revision;
        [DataMember] public string CreatedAt = DateTime.UtcNow.ToString("o"), UpdatedAt = DateTime.UtcNow.ToString("o");
        [DataMember] public string DeletedAt;
        public static Note Create() { var n = new Note(); n.Elements.Add(Element.Text("")); return n; }
        public string SearchText { get { return Title + " " + string.Join(" ", Elements.Where(e => e.Type == "text").Select(e => e.PlainText)); } }
    }
    [DataContract] public class NoteView
    {
        [DataMember] public double Left = 100, Top = 100, Width = 540, Height = 460, Zoom = 1;
        [DataMember] public bool IsOpen = true, AlwaysOnTop, Fit;
        [DataMember] public bool UiPinned;
        [DataMember(EmitDefaultValue=false)] public double? WindowOpacity;
        [DataMember] public bool SnapGrid;
        [DataMember] public bool Flow;
        [DataMember] public double PanX, PanY, BasicZoom = 1, FlowZoom;
        [DataMember] public double BasicPanX=200, BasicPanY=200, FlowPanX=200, FlowPanY=200;
        [DataMember] public bool BasicFit;
        [DataMember(EmitDefaultValue = false)] public string HoverUi, PenColor;
        [DataMember] public double PenWidth, EraserWidth;
    }
    [DataContract] public class Asset
    {
        [DataMember] public string Id, FileName, Sha256;
        [DataMember] public long Bytes;
        [DataMember] public int PixelWidth, PixelHeight;
    }
    [DataContract] public class BackupManifest
    {
        [DataMember] public int SchemaVersion = 1;
        [DataMember] public string CreatedAt = DateTime.UtcNow.ToString("o");
        [DataMember] public List<Note> Notes = new List<Note>();
        [DataMember] public List<Asset> Assets = new List<Asset>();
    }
    public static class Json
    {
        public static string Write<T>(T value)
        {
            using (var s = new MemoryStream()) { new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 500000 }).WriteObject(s, value); return Encoding.UTF8.GetString(s.ToArray()); }
        }
        public static T Read<T>(string value)
        {
            if (value == null || value.Length > 16 * 1024 * 1024) throw new InvalidDataException("문서 크기 한도를 초과했습니다.");
            using (var s = new MemoryStream(Encoding.UTF8.GetBytes(value)))
                return (T)new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 500000 }).ReadObject(s);
        }
        public static T Copy<T>(T value) { return Read<T>(Write(value)); }
    }
    public static class Rules
    {
        public static bool Id(string id) { return id != null && Regex.IsMatch(id, "^[a-f0-9]{32}$"); }
        public static bool Hash(string id) { return id != null && Regex.IsMatch(id, "^[a-f0-9]{64}$"); }
        public static bool Color(string value) { return value != null && Regex.IsMatch(value, "^#[A-Fa-f0-9]{8}$"); }
        public static bool Number(double v, double min, double max) { return !double.IsNaN(v) && !double.IsInfinity(v) && v >= min && v <= max; }
        public static void Require(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }
        public static void Validate(Note n)
        {
            Require(n != null && (n.SchemaVersion == 1 || n.SchemaVersion == 2 || n.SchemaVersion == 3 || n.SchemaVersion == 4), "지원하지 않는 문서 버전입니다. 원본은 보존됩니다.");
            Require(Id(n.Id) && n.Title != null && n.Title.Length <= 500 && Color(n.Background), "메모의 기본 정보가 올바르지 않습니다.");
            Require(Number(n.PaperWidth, 120, 8192) && Number(n.PaperHeight, 120, 8192), "종이 크기가 올바르지 않습니다.");
            Require(n.Elements != null && n.Elements.Count <= 1000, "메모 요소 개수 한도를 초과했습니다.");
            var ids = new HashSet<string>();
            long length = 0, inkPoints = 0;
            foreach (var e in n.Elements)
            {
                Require(e != null && Id(e.Id) && ids.Add(e.Id), "중복되거나 잘못된 요소 ID입니다.");
                Require(new[] { "text", "rectangle", "rounded", "ellipse", "line", "arrow", "image", "stroke" }.Contains(e.Type), "지원하지 않는 요소입니다.");
                if (e.Type == "stroke")
                {
                    Require(n.SchemaVersion >= 2 && e.Points != null && e.Points.Count > 0 && e.Points.Count <= 20000, "필기 데이터가 올바르지 않습니다.");
                    inkPoints += e.Points.Count;
                    foreach (var point in e.Points) Require(point != null && Number(point.X, 0, 1) && Number(point.Y, 0, 1), "필기 좌표가 올바르지 않습니다.");
                }
                Require(n.SchemaVersion >= 3 || (e.GroupId == null && !e.ImageOpacity.HasValue && !e.CropX.HasValue), "새 기능 문서는 버전 3이어야 합니다.");
                Require(n.SchemaVersion >= 4 || (!e.Locked && !e.IsDrawing), "새 편집 기능은 문서 버전 4가 필요합니다.");
                Require(!e.IsDrawing || e.Type == "image", "픽셀 필기 형식 오류");
                Require(e.GroupId == null || Id(e.GroupId), "그룹 ID 오류");
                Require(!e.ImageOpacity.HasValue || Number(e.ImageOpacity.Value, 0, 1), "이미지 투명도 오류");
                if (e.CropX.HasValue || e.CropY.HasValue || e.CropWidth.HasValue || e.CropHeight.HasValue) {
                    Require(e.CropX.HasValue && e.CropY.HasValue && e.CropWidth.HasValue && e.CropHeight.HasValue, "자르기 영역 누락");
                    Require(Number(e.CropX.Value, 0, 1) && Number(e.CropY.Value, 0, 1) && Number(e.CropWidth.Value, .001, 1) && Number(e.CropHeight.Value, .001, 1) && e.CropX.Value + e.CropWidth.Value <= 1.000001 && e.CropY.Value + e.CropHeight.Value <= 1.000001, "자르기 영역 오류");
                }
                Require(e.LineOrientation == null || new[] { "horizontal", "vertical", "down", "up" }.Contains(e.LineOrientation), "선 방향이 올바르지 않습니다.");
                Require(Number(e.X, 0, 8192) && Number(e.Y, 0, 8192) && Number(e.Width, 16, 8192) && Number(e.Height, 16, 8192), "요소의 위치나 크기가 올바르지 않습니다.");
                Require(e.X + e.Width <= n.PaperWidth + .1 && e.Y + e.Height <= n.PaperHeight + .1, "요소가 종이 범위를 벗어났습니다.");
                Require(Color(e.Fill) && Color(e.Stroke) && Number(e.StrokeWidth, 0, 20) && Number(e.Padding, 0, 40), "요소 서식이 올바르지 않습니다.");
                if (e.Type == "image") Require(Hash(e.AssetId), "이미지 참조가 올바르지 않습니다.");
                Require(e.Paragraphs != null, "문단 데이터가 없습니다.");
                foreach (var p in e.Paragraphs)
                {
                    Require(p != null && new[] { "Left", "Center", "Right" }.Contains(p.Align) && Number(p.LineHeight, 0, 300) && p.Runs != null, "문단 서식이 올바르지 않습니다.");
                    foreach (var r in p.Runs)
                    {
                        Require(r != null && r.Text != null && r.Font != null && r.Font.Length <= 120 && Number(r.Size, 8, 144) && Color(r.Color), "글자 서식이 올바르지 않습니다.");
                        length += r.Text.Length;
                    }
                }
            }
            Require(length <= 1000000, "메모의 글자 수 한도를 초과했습니다.");
            Require(inkPoints <= 100000, "메모의 필기 점 한도를 초과했습니다.");
        }
        public static void Validate(Asset a)
        {
            Require(a != null && Hash(a.Id) && a.Id == a.Sha256 && a.FileName != null, "첨부 정보가 올바르지 않습니다.");
            Require(Regex.IsMatch(a.FileName, "^[a-f0-9]{64}\\.(png|jpg|webp)$") && a.FileName.StartsWith(a.Id + ".", StringComparison.Ordinal), "첨부 경로가 올바르지 않습니다.");
            Require(a.Bytes > 0 && a.Bytes <= 20 * 1024 * 1024 && a.PixelWidth > 0 && a.PixelHeight > 0 && (long)a.PixelWidth * a.PixelHeight <= 25000000, "이미지 한도를 초과했습니다.");
        }
        public static void Validate(NoteView v)
        {
            Require(v != null && Number(v.Left, -100000, 100000) && Number(v.Top, -100000, 100000) && Number(v.Width, 300, 12000) && Number(v.Height, 240, 12000) && Number(v.Zoom, .1, 4), "창 설정이 올바르지 않습니다.");
            Require(!v.WindowOpacity.HasValue || Number(v.WindowOpacity.Value,.2,1), "메모 불투명도는 20~100% 범위여야 합니다.");
            foreach(double value in new[]{v.PanX,v.PanY,v.BasicPanX,v.BasicPanY,v.FlowPanX,v.FlowPanY}) Require(Number(value,0,100000), "화면 이동 값 오류");
            Require(Number(v.BasicZoom,0,4) && Number(v.FlowZoom,0,4), "모드 배율 오류");
            Require((v.HoverUi == null || v.HoverUi == "on" || v.HoverUi == "off") && (v.PenColor == null || Color(v.PenColor)) && Number(v.PenWidth, 0, 64) && Number(v.EraserWidth, 0, 128), "보기/펜 설정이 올바르지 않습니다.");
        }
    }
    public sealed class History
    {
        readonly List<string> states = new List<string>(); int index = -1;
        public History(Note n) { Record(n); }
        static string State(Note n) { var copy = Json.Copy(n); copy.Revision = 0; copy.UpdatedAt = copy.CreatedAt; return Json.Write(copy); }
        public void Record(Note n)
        {
            string state = State(n);
            if (index >= 0 && states[index] == state) return;
            if (index + 1 < states.Count) states.RemoveRange(index + 1, states.Count - index - 1);
            states.Add(state); index++;
            if (states.Count > 100) { states.RemoveAt(0); index--; }
            while (states.Count > 2 && states.Sum(s => (long)s.Length * 2) > 8L * 1024 * 1024) { states.RemoveAt(0); index--; }
        }
        public Note Undo(Note live) { Record(live); return index > 0 ? Json.Read<Note>(states[--index]) : null; }
        public Note Redo() { return index + 1 < states.Count ? Json.Read<Note>(states[++index]) : null; }
    }
}
