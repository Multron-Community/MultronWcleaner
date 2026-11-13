using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace MultronWinCleaner.Processes
{
    public static class HeaderCache
    {
        private sealed class Entry
        {
            public readonly long Ticks;
            public readonly long Size;
            public readonly bool IsProgram;
            public int LastSeenDay;

            public Entry(long ticks, long size, bool isProgram, int lastSeenDay)
            {
                Ticks = ticks; Size = size; IsProgram = isProgram; LastSeenDay = lastSeenDay;
            }
        }

        private const int MaxEntries = 2_000_000;
        private const int KeepDays = 60;
        private const string Magic = "MWCHD1";

        private static readonly object LoadLock = new();
        private static ConcurrentDictionary<string, Entry>? cache;
        private static volatile bool dirty;
        private static int entryCount;

        private static string CachePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MultronWinCleaner", "headers.bin");

        private static ConcurrentDictionary<string, Entry> Cache
        {
            get
            {
                if (cache != null) return cache;
                lock (LoadLock)
                {
                    if (cache != null) return cache;
                    var loaded = new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        if (File.Exists(CachePath))
                        {
                            using var stream = new FileStream(CachePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
                            using var reader = new BinaryReader(stream, Encoding.UTF8);
                            if (reader.ReadString() == Magic)
                            {
                                int count = reader.ReadInt32();
                                for (int i = 0; i < count; i++)
                                {
                                    string path = reader.ReadString();
                                    long ticks = reader.ReadInt64();
                                    long size = reader.ReadInt64();
                                    bool isProgram = reader.ReadBoolean();
                                    int seen = reader.ReadInt32();
                                    loaded[path] = new Entry(ticks, size, isProgram, seen);
                                }
                            }
                        }
                    }
                    catch { loaded.Clear(); }
                    entryCount = loaded.Count;
                    cache = loaded;
                    return cache;
                }
            }
        }

        public static bool? Lookup(string path, long size, long ticks)
        {
            if (!Cache.TryGetValue(path, out var entry) || entry.Size != size || entry.Ticks != ticks)
                return null;
            int today = FileFingerprint.Today;
            if (entry.LastSeenDay != today)
            {
                entry.LastSeenDay = today;
                dirty = true;
            }
            return entry.IsProgram;
        }

        public static void Remember(string path, long size, long ticks, bool isProgram)
        {
            var entry = new Entry(ticks, size, isProgram, FileFingerprint.Today);
            if (Cache.ContainsKey(path))
                Cache[path] = entry;
            else if (Volatile.Read(ref entryCount) < MaxEntries && Cache.TryAdd(path, entry))
                Interlocked.Increment(ref entryCount);
            dirty = true;
        }

        public static void Release()
        {
            lock (LoadLock)
            {
                if (dirty)
                    return;
                cache = null;
                entryCount = 0;
            }
        }

        public static void Save()
        {
            if (!dirty || cache == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                int oldest = FileFingerprint.Today - KeepDays;
                var keep = cache.Where(kv => kv.Value.LastSeenDay >= oldest).Take(MaxEntries).ToList();
                string tmp = CachePath + ".tmp";
                using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                using (var writer = new BinaryWriter(stream, Encoding.UTF8))
                {
                    writer.Write(Magic);
                    writer.Write(keep.Count);
                    foreach (var kv in keep)
                    {
                        writer.Write(kv.Key);
                        writer.Write(kv.Value.Ticks);
                        writer.Write(kv.Value.Size);
                        writer.Write(kv.Value.IsProgram);
                        writer.Write(kv.Value.LastSeenDay);
                    }
                }
                File.Move(tmp, CachePath, overwrite: true);
                foreach (var kv in cache.Where(kv => kv.Value.LastSeenDay < oldest).ToList())
                    cache.TryRemove(kv.Key, out _);
                entryCount = cache.Count;
                dirty = false;
            }
            catch { }
        }

        public static bool IsEmpty => (cache == null || cache.IsEmpty) && !File.Exists(CachePath);

        public static void Clear()
        {
            cache?.Clear();
            entryCount = 0;
            dirty = false;
            try { File.Delete(CachePath); } catch { }
        }
    }
}
