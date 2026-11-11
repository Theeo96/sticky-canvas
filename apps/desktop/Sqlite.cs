using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace StickyCanvas
{
    // The OS SQLite library is used through its stable C API, with bound UTF-8 values.
    internal sealed class Sqlite : IDisposable
    {
        IntPtr db;
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_open_v2(byte[] name, out IntPtr db, int flags, IntPtr vfs);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_close_v2(IntPtr db);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr sqlite3_errmsg(IntPtr db);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bytes, out IntPtr stmt, IntPtr tail);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] text, int bytes, IntPtr destructor);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr stmt);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_text(IntPtr stmt, int index);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_column_bytes(IntPtr stmt, int index);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_busy_timeout(IntPtr db, int ms);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr sqlite3_backup_init(IntPtr dest, byte[] destName, IntPtr source, byte[] sourceName);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_backup_step(IntPtr backup, int pages);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_backup_finish(IntPtr backup);
        static byte[] Utf8(string s) { return Encoding.UTF8.GetBytes(s + "\0"); }
        string Error() { return Marshal.PtrToStringAnsi(sqlite3_errmsg(db)); }
        public Sqlite(string path)
        {
            int rc = sqlite3_open_v2(Utf8(path), out db, 2 | 4 | 0x10000, IntPtr.Zero);
            if (rc != 0) { string message = db == IntPtr.Zero ? "DB 열기 실패" : Error(); Dispose(); throw new IOException(message); }
            sqlite3_busy_timeout(db, 1500);
        }
        public List<string[]> Query(string sql, int columns, params string[] args)
        {
            IntPtr stmt;
            if (sqlite3_prepare_v2(db, Utf8(sql), -1, out stmt, IntPtr.Zero) != 0) throw new IOException(Error());
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(args[i] ?? "");
                    if (sqlite3_bind_text(stmt, i + 1, bytes, bytes.Length, new IntPtr(-1)) != 0) throw new IOException(Error());
                }
                var result = new List<string[]>(); int rc;
                while ((rc = sqlite3_step(stmt)) == 100)
                {
                    var row = new string[columns];
                    for (int i = 0; i < columns; i++)
                    {
                        IntPtr ptr = sqlite3_column_text(stmt, i); int count = sqlite3_column_bytes(stmt, i);
                        var bytes = new byte[count]; if (count > 0) Marshal.Copy(ptr, bytes, 0, count);
                        row[i] = Encoding.UTF8.GetString(bytes);
                    }
                    result.Add(row);
                }
                if (rc != 101) throw new IOException(Error());
                return result;
            }
            finally { sqlite3_finalize(stmt); }
        }
        public void Execute(string sql, params string[] args) { Query(sql, 0, args); }
        public void Transaction(Action work)
        {
            Execute("BEGIN IMMEDIATE");
            try { work(); Execute("COMMIT"); }
            catch { try { Execute("ROLLBACK"); } catch { } throw; }
        }
        public void Backup(string path)
        {
            using (var dest = new Sqlite(path))
            {
                IntPtr backup = sqlite3_backup_init(dest.db, Utf8("main"), db, Utf8("main"));
                if (backup == IntPtr.Zero) throw new IOException(dest.Error());
                int step, finish;
                try { step = sqlite3_backup_step(backup, -1); }
                finally { finish = sqlite3_backup_finish(backup); }
                if (step != 101 || finish != 0) throw new IOException("일관된 DB 백업을 만들지 못했습니다.");
                if (dest.Query("PRAGMA quick_check", 1)[0][0] != "ok") throw new IOException("백업 무결성 검사 실패");
            }
        }
        public void Dispose() { if (db != IntPtr.Zero) { sqlite3_close_v2(db); db = IntPtr.Zero; } }
    }
}
