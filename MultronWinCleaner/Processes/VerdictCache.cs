using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace MultronWinCleaner.Processes
{
    public static class VerdictCache
    {
        private sealed record Entry(long CheckedTicks, bool Uploaded);

        private const int MaxEntries = 1_000_000;
        private const int UploadedTrustDivisor = 4;
        private static readonly TimeSpan KeepFor = TimeSpan.FromDays(45);

        private static readonly object LoadLock = new();
        private static ConcurrentDictionary<string, Entry>? cache;
        private static volatile bool dirty;
        private static int entryCount;

        private static string CachePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MultronWinCleaner", "verdicts.cache");

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
                            foreach (string line in File.ReadLines(CachePath))
                            {
                                var parts = line.Split('\t');
                                if (parts.Length != 3 || parts[0].Length != 64) continue;
                                if (!long.TryParse(parts[1], out long ticks)) continue;
                                loaded[parts[0]] = new Entry(ticks, parts[2] == "1");
                            }
                        }
                    }
                    catch { }
                    entryCount = loaded.Count;
                    cache = loaded;
                    return cache;
                }
            }
        }

        public static bool TryGetClean(string sha256, TimeSpan trust, out TimeSpan age)
        {
            age = TimeSpan.Zero;
            if (trust <= TimeSpan.Zero || sha256.Length != 64 || !Cache.TryGetValue(sha256, out var entry))
                return false;
            age = DateTime.UtcNow - new DateTime(entry.CheckedTicks, DateTimeKind.Utc);
            TimeSpan limit = entry.Uploaded ? trust / UploadedTrustDivisor : trust;
            return age >= TimeSpan.Zero && age < limit;
        }

        public static void RememberClean(string sha256, bool uploaded)
        {
            if (sha256.Length != 64) return;
            var entry = new Entry(DateTime.UtcNow.Ticks, uploaded);
            if (Cache.ContainsKey(sha256))
                Cache[sha256] = entry;
            else if (Volatile.Read(ref entryCount) < MaxEntries && Cache.TryAdd(sha256, entry))
                Interlocked.Increment(ref entryCount);
            dirty = true;
        }

        public static void Forget(string sha256)
        {
            if (Cache.TryRemove(sha256, out _))
            {
                Interlocked.Decrement(ref entryCount);
                dirty = true;
            }
        }

        public static void Save()
        {
            if (!dirty || cache == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                long oldest = (DateTime.UtcNow - KeepFor).Ticks;
                var sb = new StringBuilder();
                foreach (var kv in cache.Where(kv => kv.Value.CheckedTicks >= oldest).Take(MaxEntries))
                    sb.Append(kv.Key).Append('\t').Append(kv.Value.CheckedTicks).Append('\t').Append(kv.Value.Uploaded ? '1' : '0').Append('\n');
                string tmp = CachePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Encoding.ASCII);
                File.Move(tmp, CachePath, overwrite: true);
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
