using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MultronWinCleaner.Processes
{
    public static class FileFingerprint
    {
        public readonly struct Print
        {
            public readonly string Sha256;
            public readonly long Size;
            public readonly double Entropy;
            public readonly bool FromCache;
            public Print(string sha256, long size, double entropy, bool fromCache)
            {
                Sha256 = sha256; Size = size; Entropy = entropy; FromCache = fromCache;
            }
        }

        private sealed record Entry(long Ticks, long Size, string Sha256, double Entropy);

        private const int BufferSize = 1 << 20;
        private const int EntropySample = 1 << 20;
        private const int MaxCacheEntries = 1_000_000;

        private static readonly object LoadLock = new();
        private static ConcurrentDictionary<string, Entry>? cache;
        private static volatile bool dirty;

        private static string CachePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MultronWinCleaner", "fingerprints.cache");

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
                                var parts = line.Split('\t', 5);
                                if (parts.Length != 5 || parts[2].Length != 64) continue;
                                if (!long.TryParse(parts[0], out long ticks) || !long.TryParse(parts[1], out long size)) continue;
                                if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double entropy)) entropy = -1;
                                loaded[parts[4]] = new Entry(ticks, size, parts[2], entropy);
                            }
                        }
                    }
                    catch { }
                    cache = loaded;
                    return cache;
                }
            }
        }

        public static Print? TryGet(string path, bool measureEntropy, bool useCache)
        {
            long size, ticks;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return null;
                size = info.Length;
                ticks = info.LastWriteTimeUtc.Ticks;
            }
            catch { return null; }

            bool wantEntropy = measureEntropy && size >= 2048;
            if (useCache && Cache.TryGetValue(path, out var hit) && hit.Size == size && hit.Ticks == ticks
                && (!wantEntropy || hit.Entropy >= 0))
                return new Print(hit.Sha256, size, wantEntropy ? hit.Entropy : -1, true);

            try
            {
                using var stream = ForceDelete.OpenRead(path, BufferSize, useAsync: false);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var counts = wantEntropy ? new long[256] : null;
                long sampled = 0, total = 0;
                byte[] buffer = new byte[BufferSize];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    sha.AppendData(buffer, 0, read);
                    total += read;
                    if (counts != null && sampled < EntropySample)
                    {
                        int take = (int)Math.Min(read, EntropySample - sampled);
                        for (int i = 0; i < take; i++)
                            counts[buffer[i]]++;
                        sampled += take;
                    }
                }

                double entropy = -1;
                if (counts != null && sampled > 0)
                {
                    entropy = 0;
                    for (int i = 0; i < 256; i++)
                    {
                        if (counts[i] == 0) continue;
                        double p = (double)counts[i] / sampled;
                        entropy -= p * Math.Log2(p);
                    }
                }

                string hash = Convert.ToHexString(sha.GetHashAndReset());
                if (useCache && Cache.Count < MaxCacheEntries)
                {
                    Cache[path] = new Entry(ticks, total, hash, entropy);
                    dirty = true;
                }
                return new Print(hash, total, entropy, false);
            }
            catch
            {
                return null;
            }
        }

        public static void Save()
        {
            if (!dirty || cache == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                var sb = new StringBuilder();
                foreach (var kv in cache.Take(MaxCacheEntries))
                {
                    sb.Append(kv.Value.Ticks).Append('\t').Append(kv.Value.Size).Append('\t').Append(kv.Value.Sha256).Append('\t')
                      .Append(kv.Value.Entropy.ToString("R", CultureInfo.InvariantCulture)).Append('\t').Append(kv.Key).Append('\n');
                }
                string tmp = CachePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                File.Move(tmp, CachePath, overwrite: true);
                dirty = false;
            }
            catch { }
        }

        public static void Clear()
        {
            cache?.Clear();
            dirty = false;
            try { File.Delete(CachePath); } catch { }
        }
    }
}
