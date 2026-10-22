using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace MultronWinCleaner.Processes
{
    public static class SecureDelete
    {
        public enum Mode { Off = 0, Auto = 1, OnePass = 2, ThreePass = 3, SevenPass = 4 }

        private const string ShredSettingKey = "shredmode";

        public static Mode ReadConfiguredMode()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.txt");
                if (!File.Exists(path)) return Mode.Off;
                string prefix = ShredSettingKey + ":";
                foreach (string raw in File.ReadLines(path))
                {
                    string line = raw.Trim();
                    if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(line.Substring(prefix.Length).Trim(), out int v))
                        return (Mode)Math.Clamp(v, 0, 4);
                }
            }
            catch { }
            return Mode.Off;
        }

        public static string ModeName(Mode mode) => mode switch
        {
            Mode.Auto => Loc.T("Automatic — SSD-aware (recommended)"),
            Mode.OnePass => Loc.T("1 pass (fast)"),
            Mode.ThreePass => Loc.T("3 passes (DoD 5220.22-M)"),
            Mode.SevenPass => Loc.T("7 passes (thorough)"),
            _ => Loc.T("Off — normal delete (fastest)"),
        };

        public static string Describe(Mode mode, string samplePath = @"C:\")
        {
            bool ssd = IsSolidState(samplePath);
            string drive = (Path.GetPathRoot(samplePath) ?? @"C:\").TrimEnd('\\');
            return Loc.F("{0} — {1} pass(es) on {2} ({3})", ModeName(mode), PassesFor(mode, ssd), drive, ssd ? "SSD" : "HDD");
        }

        public static int PassesFor(Mode mode, bool ssd) => mode switch
        {
            Mode.OnePass => 1,
            Mode.ThreePass => 3,
            Mode.SevenPass => 7,
            Mode.Auto => ssd ? 1 : 3,
            _ => 0,
        };

        public static bool Shred(string path, Mode mode, Action<string>? progress = null)
        {
            try { path = Path.GetFullPath(path); } catch { return false; }
            if (mode == Mode.Off || !File.Exists(path))
                return ForceDelete.TryHardDelete(path);

            try { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return ForceDelete.TryHardDelete(path); }
            catch { }

            bool ssd = IsSolidState(path);
            int passes = PassesFor(mode, ssd);

            try
            {
                Overwrite(path, passes, progress);
            }
            catch {  }

            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    string renamed = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".tmp");
                    File.Move(path, renamed);
                    path = renamed;
                }
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
                fs.SetLength(0);
            }
            catch { }

            return ForceDelete.TryHardDelete(path);
        }

        private static void Overwrite(string path, int passes, Action<string>? progress)
        {
            if (passes <= 0) return;

            try
            {
                var attrs = File.GetAttributes(path);
                if ((attrs & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                    File.SetAttributes(path, FileAttributes.Normal);
            }
            catch {  }

            progress?.Invoke(Loc.F("Shredding ({0} pass(es))...", passes));

            bool any = false;
            foreach (var (streamPath, length) in EnumerateDataStreams(path))
            {
                any = true;
                OverwriteStream(streamPath, length, passes);
            }
            if (!any)
            {
                try { OverwriteStream(path, new FileInfo(path).Length, passes); }
                catch { }
            }
        }

        private static void OverwriteStream(string streamPath, long length, int passes)
        {
            if (length <= 0) return;
            try
            {
                var handle = ForceDelete.OpenForOverwrite(streamPath, writeThrough: true);
                using var stream = handle != null
                    ? new FileStream(handle, FileAccess.Write, 1 << 20)
                    : new FileStream(streamPath, FileMode.Open, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.WriteThrough);
                byte[] buffer = new byte[1 << 20];
                for (int pass = 0; pass < passes; pass++)
                {
                    bool zeros = passes > 1 && pass == passes - 1;
                    stream.Position = 0;
                    long remaining = length;
                    while (remaining > 0)
                    {
                        int chunk = (int)Math.Min(buffer.Length, remaining);
                        if (zeros)
                            Array.Clear(buffer, 0, chunk);
                        else
                            RandomNumberGenerator.Fill(buffer.AsSpan(0, chunk));
                        stream.Write(buffer, 0, chunk);
                        remaining -= chunk;
                    }
                    stream.Flush(flushToDisk: true);
                }
                try { stream.SetLength(0); stream.Flush(flushToDisk: true); }
                catch { }
            }
            catch { }
        }

        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WIN32_FIND_STREAM_DATA
        {
            public long StreamSize;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)] public string cStreamName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstStreamW(string lpFileName, int InfoLevel, out WIN32_FIND_STREAM_DATA data, int dwFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool FindNextStreamW(IntPtr hFindStream, out WIN32_FIND_STREAM_DATA data);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FindClose(IntPtr hFindFile);

        private static IEnumerable<(string Path, long Length)> EnumerateDataStreams(string path)
        {
            IntPtr handle = FindFirstStreamW(path, 0 , out WIN32_FIND_STREAM_DATA data, 0);
            if (handle == InvalidHandle)
                yield break;
            try
            {
                do
                {
                    string name = data.cStreamName ?? "";
                    long length = data.StreamSize;
                    string streamPath;
                    if (name.Length == 0 || name.Equals("::$DATA", StringComparison.OrdinalIgnoreCase))
                    {
                        streamPath = path;
                    }
                    else
                    {
                        int lastColon = name.LastIndexOf(':');
                        string shortName = lastColon > 0 ? name.Substring(0, lastColon) : name;
                        streamPath = path + shortName;
                    }
                    yield return (streamPath, length);
                }
                while (FindNextStreamW(handle, out data));
            }
            finally
            {
                FindClose(handle);
            }
        }


        private static readonly ConcurrentDictionary<char, bool> SsdCache = new();

        public static bool IsSolidState(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                if (full.Length < 2 || full[1] != ':') return false;
                char drive = char.ToUpperInvariant(full[0]);
                return SsdCache.GetOrAdd(drive, QuerySeekPenalty);
            }
            catch { return false; }
        }

        private const int FILE_SHARE_READ = 0x1, FILE_SHARE_WRITE = 0x2, OPEN_EXISTING = 3;
        private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
        private const int StorageDeviceSeekPenaltyProperty = 7;
        private const int PropertyStandardQuery = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct STORAGE_PROPERTY_QUERY
        {
            public int PropertyId;
            public int QueryType;
            public byte AdditionalParameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVICE_SEEK_PENALTY_DESCRIPTOR
        {
            public uint Version;
            public uint Size;
            [MarshalAs(UnmanagedType.U1)] public bool IncursSeekPenalty;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, int dwShareMode,
            IntPtr lpSecurityAttributes, int dwCreationDisposition, int dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode,
            ref STORAGE_PROPERTY_QUERY lpInBuffer, int nInBufferSize,
            ref DEVICE_SEEK_PENALTY_DESCRIPTOR lpOutBuffer, int nOutBufferSize,
            out uint lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private static bool QuerySeekPenalty(char drive)
        {
            IntPtr handle = CreateFileW($@"\\.\{drive}:", 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                return false;
            try
            {
                var query = new STORAGE_PROPERTY_QUERY { PropertyId = StorageDeviceSeekPenaltyProperty, QueryType = PropertyStandardQuery };
                var descriptor = new DEVICE_SEEK_PENALTY_DESCRIPTOR();
                if (DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY, ref query, Marshal.SizeOf<STORAGE_PROPERTY_QUERY>(),
                        ref descriptor, Marshal.SizeOf<DEVICE_SEEK_PENALTY_DESCRIPTOR>(), out _, IntPtr.Zero))
                    return !descriptor.IncursSeekPenalty;
            }
            catch { }
            finally { CloseHandle(handle); }
            return false;
        }
    }
}
