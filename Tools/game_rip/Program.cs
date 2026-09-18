using System;
using System.IO;
using System.Runtime.InteropServices;

// Reads the game's encrypted meta database and prints matching rows.
//
// The database is a SQLite3MC (multiple-ciphers) file: open, select cipher 3, then apply the key, exactly as the
// viewer's UmaDatabaseController does. The key is DBKey with the first 13 bytes of DBBaseKey XORed over it.

internal static class Program
{
    private const string Dll = "sqlite3mc_x64.dll";
    private const int SQLITE_OK = 0;
    private const int SQLITE_ROW = 100;
    private const int SQLITE_OPEN_READONLY = 1;

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_open_v2")]
    private static extern int sqlite3_open_v2(string filename, out IntPtr db, int flags, IntPtr vfs);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3mc_config")]
    private static extern int sqlite3mc_config(IntPtr db, string paramName, int newValue);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_key")]
    private static extern int sqlite3_key(IntPtr db, byte[] key, int keyLength);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_prepare_v2")]
    private static extern int sqlite3_prepare_v2(IntPtr db, string sql, int bytes, out IntPtr stmt, IntPtr tail);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_step")]
    private static extern int sqlite3_step(IntPtr stmt);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_text")]
    private static extern IntPtr sqlite3_column_text(IntPtr stmt, int column);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_int64")]
    private static extern long sqlite3_column_int64(IntPtr stmt, int column);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_errmsg")]
    private static extern IntPtr sqlite3_errmsg(IntPtr db);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_finalize")]
    private static extern int sqlite3_finalize(IntPtr stmt);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_close")]
    private static extern int sqlite3_close(IntPtr db);

    private static string Text(IntPtr stmt, int column)
    {
        IntPtr pointer = sqlite3_column_text(stmt, column);
        return pointer == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(pointer) ?? "";
    }

    private static byte[] XorKey(byte[] key, byte[] baseKey)
    {
        var result = (byte[])key.Clone();
        for (int i = 0; i < result.Length; i++)
            result[i] ^= baseKey[i % 13];
        return result;
    }

    private static int Main(string[] args)
    {
        string dbPath = args.Length > 0 ? args[0] : @"C:\Users\user\Umamusume\umamusume_Data\Persistent\meta";
        string like = args.Length > 1 ? args[1] : "%";
        int limit = args.Length > 2 ? int.Parse(args[2]) : 40;

        // Config.cs
        byte[] baseKey = { 0xF1, 0x70, 0xCE, 0xA4, 0xDF, 0xCE, 0xA3, 0xE1, 0xA5, 0xD8, 0xC7, 0x0B, 0xD1 };
        byte[] dbKey =
        {
            0x6D, 0x5B, 0x65, 0x33, 0x63, 0x36, 0x63, 0x25, 0x54, 0x71, 0x2D, 0x73, 0x50, 0x53, 0x63, 0x38,
            0x6D, 0x34, 0x37, 0x7B, 0x35, 0x63, 0x70, 0x23, 0x37, 0x34, 0x53, 0x29, 0x73, 0x43, 0x36, 0x33
        };

        byte[] key = XorKey(dbKey, baseKey);
        Console.WriteLine($"key ({key.Length} bytes): {BitConverter.ToString(key).Replace("-", " ")}");

        int rc = sqlite3_open_v2(dbPath, out IntPtr db, SQLITE_OPEN_READONLY, IntPtr.Zero);
        if (rc != SQLITE_OK && rc != SQLITE_OPEN_READONLY)
        {
            rc = sqlite3_open_v2(dbPath, out db, 2, IntPtr.Zero); // READWRITE
        }
        if (db == IntPtr.Zero)
        {
            Console.WriteLine($"open failed rc={rc}");
            return 1;
        }
        Console.WriteLine($"cipher config rc={sqlite3mc_config(db, "cipher", 3)}");
        Console.WriteLine($"key rc={sqlite3_key(db, key, key.Length)}");

        string sql = $"SELECT m,n,h,c,d,e FROM a WHERE m LIKE '{like}' OR n LIKE '{like}' LIMIT {limit}";
        rc = sqlite3_prepare_v2(db, sql, -1, out IntPtr stmt, IntPtr.Zero);
        if (rc != SQLITE_OK)
        {
            Console.WriteLine($"prepare rc={rc}: {Marshal.PtrToStringUTF8(sqlite3_errmsg(db))}");
            return 1;
        }

        int rows = 0;
        while (sqlite3_step(stmt) == SQLITE_ROW)
        {
            rows++;
            Console.WriteLine($"m={Text(stmt, 0)}\tn={Text(stmt, 1)}\th={Text(stmt, 2)}\tc={Text(stmt, 3)}"
                              + $"\td={Text(stmt, 4)}\te={sqlite3_column_int64(stmt, 5)}");
        }
        sqlite3_finalize(stmt);
        sqlite3_close(db);
        Console.WriteLine($"{rows} row(s)");
        return 0;
    }
}

