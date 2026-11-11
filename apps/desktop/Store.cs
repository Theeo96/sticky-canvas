using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace StickyCanvas
{
    public sealed class Store : IDisposable
    {
        readonly object gate = new object(); readonly Sqlite db;
        public readonly string Root;
        public readonly List<string> ReadErrors = new List<string>();
        public Store(string root)
        {
            Root = Path.GetFullPath(root);
            Directory.CreateDirectory(Root); Directory.CreateDirectory(Path.Combine(Root, "assets"));
            Directory.CreateDirectory(Path.Combine(Root, "backups"));
            db = new Sqlite(Path.Combine(Root, "notes.sqlite3"));
            try
            {
                db.Query("PRAGMA journal_mode=WAL", 1); db.Execute("PRAGMA synchronous=FULL");
                db.Execute("CREATE TABLE IF NOT EXISTS notes(id TEXT PRIMARY KEY, json TEXT NOT NULL)");
                db.Execute("CREATE TABLE IF NOT EXISTS views(id TEXT PRIMARY KEY, json TEXT NOT NULL)");
                db.Execute("CREATE TABLE IF NOT EXISTS assets(id TEXT PRIMARY KEY, json TEXT NOT NULL)");
                if (db.Query("PRAGMA quick_check", 1)[0][0] != "ok") throw new IOException("DB 무결성 검사에 실패했습니다. 원본을 보존하고 백업에서 복구하세요.");
            }
            catch { db.Dispose(); throw; }
        }
        public List<Note> Load()
        {
            lock (gate)
            {
                var result = new List<Note>();
                foreach (var row in db.Query("SELECT id,json FROM notes", 2))
                {
                    try { var n = Json.Read<Note>(row[1]); Rules.Validate(n); Rules.Require(n.Id == row[0], "메모 ID 불일치"); result.Add(n); }
                    catch (Exception) { ReadErrors.Add("메모 " + row[0] + ": 형식 오류 또는 미지원 버전. 원본 DB는 보존됩니다."); }
                }
                return result;
            }
        }
        public NoteView View(string id)
        {
            lock (gate)
            {
                try { var rows = db.Query("SELECT json FROM views WHERE id=?", 1, id); if (rows.Count > 0) { var v = Json.Read<NoteView>(rows[0][0]); Rules.Validate(v); return v; } }
                catch { ReadErrors.Add("창 설정을 초기화했습니다: " + id); }
                return new NoteView();
            }
        }
        public void Save(Note note, NoteView view)
        {
            Rules.Validate(note); Rules.Validate(view);
            lock (gate)
            {
                foreach (var element in note.Elements.Where(e => e.Type == "image"))
                    Rules.Require(GetAsset(element.AssetId) != null, "첨부 정보가 없어 저장을 중단했습니다.");
                db.Transaction(delegate {
                    db.Execute("INSERT OR REPLACE INTO notes(id,json) VALUES(?,?)", note.Id, Json.Write(note));
                    db.Execute("INSERT OR REPLACE INTO views(id,json) VALUES(?,?)", note.Id, Json.Write(view));
                });
            }
        }
        public Asset GetAsset(string id)
        {
            Rules.Require(Rules.Hash(id), "이미지 ID 오류");
            lock (gate)
            {
                var rows = db.Query("SELECT json FROM assets WHERE id=?", 1, id);
                if (rows.Count == 0) return null;
                var a = Json.Read<Asset>(rows[0][0]); Rules.Validate(a); return a;
            }
        }
        public string AssetPath(Asset asset) { Rules.Validate(asset); return Path.Combine(Root, "assets", asset.FileName); }
        public static string Sha(Stream stream)
        {
            using (var h = SHA256.Create()) return string.Concat(h.ComputeHash(stream).Select(b => b.ToString("x2")));
        }
        static void CopyLimited(Stream input, Stream output, long limit)
        {
            var buffer = new byte[65536]; long total = 0; int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                total += count; Rules.Require(total <= limit, "파일의 실제 데이터가 허용 크기를 초과했습니다."); output.Write(buffer, 0, count);
            }
        }
        public Asset ImportImage(string path)
        {
            var info = new FileInfo(path);
            Rules.Require(info.Exists && info.Length > 0 && info.Length <= 20 * 1024 * 1024, "이미지는 20 MiB 이하 파일만 넣을 수 있습니다.");
            string ext = info.Extension.ToLowerInvariant(); if (ext == ".jpeg") ext = ".jpg";
            Rules.Require(new[] { ".png", ".jpg", ".webp" }.Contains(ext), "PNG, JPEG, WebP 이미지만 지원합니다.");
            string hash; int width, height;
            using (var input = File.OpenRead(path))
            {
                var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
                Rules.Require(decoder.Frames.Count == 1, "정지 이미지 한 장만 지원합니다.");
                var f = decoder.Frames[0]; width = f.PixelWidth; height = f.PixelHeight;
                Rules.Require(width > 0 && height > 0 && (long)width * height <= 25000000, "이미지 픽셀 수는 25메가픽셀 이하여야 합니다.");
                input.Position = 0; hash = Sha(input);
            }
            var a = new Asset { Id = hash, Sha256 = hash, FileName = hash + ext, Bytes = info.Length, PixelWidth = width, PixelHeight = height };
            Rules.Validate(a);
            lock (gate)
            {
                var existing = GetAsset(hash);
                if (existing != null && File.Exists(AssetPath(existing))) return existing;
                string dest = AssetPath(a), temp = dest + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var input = File.OpenRead(path)) using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { CopyLimited(input, output, a.Bytes); output.Flush(true); }
                    using (var verify = File.OpenRead(temp)) Rules.Require(Sha(verify) == a.Sha256, "이미지 복사 중 파일이 변경됐습니다.");
                    if (File.Exists(dest)) File.Delete(temp); else File.Move(temp, dest);
                    db.Execute("INSERT OR REPLACE INTO assets(id,json) VALUES(?,?)", a.Id, Json.Write(a));
                    return a;
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }
        public int BitmapDecodeCount {get;private set;}
        public long BitmapCacheBytes {get{return 0;}}
        public BitmapSource Bitmap(string id, int maxWidth = 1600)
        {
            var a = GetAsset(id); if (a == null) throw new FileNotFoundException("첨부 정보를 찾을 수 없습니다.");
            using (var input = File.OpenRead(AssetPath(a))) {
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = Math.Min(maxWidth, a.PixelWidth); bitmap.StreamSource = input; bitmap.EndInit(); bitmap.Freeze();BitmapDecodeCount++;return bitmap;
            }
        }
        public void Export(string path, List<Note> liveSnapshot = null)
        {
            lock (gate)
            {
                var notes = liveSnapshot ?? Load();
                Rules.Require(ReadErrors.Count == 0, "읽지 못한 문서가 있습니다. 완전한 백업으로 오인하지 않도록 원본 DB 복구본을 먼저 보관하세요.");
                foreach (var note in notes) Rules.Validate(note);
                var manifest = new BackupManifest { Notes = notes };
                foreach (string id in notes.SelectMany(n => n.Elements).Where(e => e.Type == "image").Select(e => e.AssetId).Distinct())
                {
                    var a = GetAsset(id); Rules.Require(a != null && File.Exists(AssetPath(a)), "백업할 첨부가 없습니다."); manifest.Assets.Add(a);
                }
                string manifestJson = Json.Write(manifest);
                Rules.Require(Encoding.UTF8.GetByteCount(manifestJson) <= 16 * 1024 * 1024, "이 버전은 백업의 문서 정보 16 MiB까지 지원합니다. 원본 데이터는 보존됩니다.");
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite))
                    {
                        using (var zip = new ZipArchive(f, ZipArchiveMode.Create, true))
                        {
                            var entry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
                            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false))) writer.Write(manifestJson);
                            foreach (var a in manifest.Assets)
                            {
                                using (var check = File.OpenRead(AssetPath(a))) Rules.Require(Sha(check) == a.Sha256, "첨부 무결성 검사 실패");
                                zip.CreateEntryFromFile(AssetPath(a), "assets/" + a.FileName, CompressionLevel.NoCompression);
                            }
                        }
                        f.Flush(true);
                    }
                    if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }
        public List<Note> Import(string path)
        {
            string staging = Path.Combine(Path.GetTempPath(), "StickyCanvas-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try { return ImportValidated(path, staging); }
            finally { foreach (var file in Directory.GetFiles(staging)) File.Delete(file); Directory.Delete(staging); }
        }
        List<Note> ImportValidated(string path, string staging)
        {
            Rules.Require(new FileInfo(path).Length <= 512L * 1024 * 1024, "백업 파일은 512 MiB 이하여야 합니다.");
            var staged = new Dictionary<string, string>(); BackupManifest manifest;
            using (var file = File.OpenRead(path)) using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
            {
                Rules.Require(zip.Entries.Count <= 10002, "백업 항목이 너무 많습니다.");
                var names = new HashSet<string>(StringComparer.Ordinal);
                long total = 0;
                foreach (var e in zip.Entries)
                {
                    Rules.Require(names.Add(e.FullName), "백업에 중복 경로가 있습니다.");
                    Rules.Require(e.FullName == "manifest.json" || System.Text.RegularExpressions.Regex.IsMatch(e.FullName, "^assets/[a-f0-9]{64}\\.(png|jpg|webp)$"), "백업에 허용하지 않는 경로가 있습니다.");
                    total += e.Length; Rules.Require(total <= 512L * 1024 * 1024 && e.Length <= (e.FullName == "manifest.json" ? 16 : 20) * 1024L * 1024, "백업 압축 해제 한도를 초과했습니다.");
                }
                var main = zip.GetEntry("manifest.json"); Rules.Require(main != null, "백업 명세가 없습니다.");
                using (var input = main.Open()) using (var bytes = new MemoryStream()) { CopyLimited(input, bytes, 16 * 1024 * 1024); manifest = Json.Read<BackupManifest>(Encoding.UTF8.GetString(bytes.ToArray())); }
                Rules.Require(manifest != null && manifest.SchemaVersion == 1 && manifest.Notes != null && manifest.Assets != null, "지원하지 않는 백업 버전입니다.");
                Rules.Require(manifest.Notes.Count <= 5000 && manifest.Assets.Count <= 10000, "백업 항목 한도 초과");
                var ids = new HashSet<string>(); foreach (var n in manifest.Notes) { Rules.Validate(n); Rules.Require(ids.Add(n.Id), "중복 메모입니다."); }
                var assets = new HashSet<string>();
                foreach (var a in manifest.Assets)
                {
                    Rules.Validate(a); Rules.Require(assets.Add(a.Id), "중복 첨부입니다.");
                    var e = zip.GetEntry("assets/" + a.FileName); Rules.Require(e != null && e.Length == a.Bytes, "첨부가 누락되었거나 크기가 다릅니다.");
                    string stagedFile = Path.Combine(staging, a.Id);
                    using (var s = e.Open()) using (var m = new FileStream(stagedFile, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                    {
                        CopyLimited(s, m, a.Bytes); Rules.Require(m.Length == a.Bytes, "첨부의 실제 크기가 다릅니다."); m.Position = 0; Rules.Require(Sha(m) == a.Sha256, "백업 이미지 체크섬이 다릅니다."); staged[a.Id] = stagedFile;
                    }
                }
                Rules.Require(zip.Entries.Count == manifest.Assets.Count + 1, "명세와 실제 백업 항목이 다릅니다.");
                foreach (var e in manifest.Notes.SelectMany(n => n.Elements).Where(e => e.Type == "image")) Rules.Require(assets.Contains(e.AssetId), "참조 이미지가 없습니다.");
            }
            // Nothing in the destination DB is changed until every record and checksum passes validation.
            lock (gate)
            {
                var copies = manifest.Notes.Select(n => {
                    var copy = Json.Copy(n); copy.Id = Guid.NewGuid().ToString("N"); copy.Title = (copy.Title.Length > 485 ? copy.Title.Substring(0, 485) : copy.Title) + " (가져온 사본)";
                    copy.Revision = 1; copy.CreatedAt = copy.UpdatedAt = DateTime.UtcNow.ToString("o");
                    foreach (var e in copy.Elements) e.Id = Guid.NewGuid().ToString("N"); return copy;
                }).ToList();
                foreach (var a in manifest.Assets)
                {
                    string dest = AssetPath(a);
                    if (!File.Exists(dest)) { string temp = dest + "." + Guid.NewGuid().ToString("N") + ".tmp"; try { using (var input = File.OpenRead(staged[a.Id])) using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { CopyLimited(input, output, a.Bytes); output.Flush(true); } File.Move(temp, dest); } finally { if (File.Exists(temp)) File.Delete(temp); } }
                    else using (var s = File.OpenRead(dest)) Rules.Require(Sha(s) == a.Sha256, "기존 첨부 파일이 손상돼 가져오기를 중단했습니다.");
                }
                db.Transaction(delegate {
                    foreach (var a in manifest.Assets) db.Execute("INSERT OR IGNORE INTO assets(id,json) VALUES(?,?)", a.Id, Json.Write(a));
                    foreach (var n in copies)
                    {
                        db.Execute("INSERT INTO notes(id,json) VALUES(?,?)", n.Id, Json.Write(n));
                        db.Execute("INSERT INTO views(id,json) VALUES(?,?)", n.Id, Json.Write(new NoteView { IsOpen = false }));
                    }
                });
                return copies;
            }
        }
        public void RecoverySnapshot()
        {
            lock (gate)
            {
                string dir = Path.Combine(Root, "backups"), path = Path.Combine(dir, "recovery-" + DateTime.Now.ToString("yyyy-MM-dd") + ".sqlite3");
                if (File.Exists(path)) return;
                string temp = path + ".tmp";
                try { if (File.Exists(temp)) File.Delete(temp); db.Backup(temp); File.Move(temp, path); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                // Only this app's dated DB recovery snapshots are rotated. Assets are never purged automatically.
                foreach (var old in Directory.GetFiles(dir, "recovery-????-??-??.sqlite3").OrderByDescending(p => p).Skip(7)) File.Delete(old);
            }
        }
        public void Dispose() { lock(gate) db.Dispose(); }
    }
}
