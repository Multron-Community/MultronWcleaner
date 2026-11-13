using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

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

        private sealed class Entry
        {
            public readonly long Ticks;
            public readonly long Size;
            public readonly byte[] Hash;
            public readonly double Entropy;
            public int LastSeenDay;

            public Entry(long ticks, long size, byte[] hash, double entropy, int lastSeenDay)
            {
                Ticks = ticks; Size = size; Hash = hash; Entropy = entropy; LastSeenDay = lastSeenDay;
            }
        }

        private const int BufferSize = 1 << 20;
        private const int EntropySample = 1 << 20;
        private const int MaxCacheEntries = 1_000_000;
        private const int KeepDays = 60;
        private const string Magic = "MWCFP2";

        private static readonly object LoadLock = new();
        private static ConcurrentDictionary<string, Entry>? cache;
        private static volatile bool dirty;
        private static int entryCount;

        private static string CacheFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MultronWinCleaner");
        private static string CachePath => Path.Combine(CacheFolder, "fingerprints.bin");
        private static string LegacyCachePath => Path.Combine(CacheFolder, "fingerprints.cache");

        internal static int Today => (int)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerDay);

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
                            LoadBinary(loaded);
                        else if (File.Exists(LegacyCachePath))
                        {
                            LoadLegacy(loaded);
                            dirty = loaded.Count > 0;
                        }
                    }
                    catch { loaded.Clear(); }
                    entryCount = loaded.Count;
                    cache = loaded;
                    return cache;
                }
            }
        }

        private static void LoadBinary(ConcurrentDictionary<string, Entry> into)
        {
            using var stream = new FileStream(CachePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (reader.ReadString() != Magic) return;
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                string path = reader.ReadString();
                long ticks = reader.ReadInt64();
                long size = reader.ReadInt64();
                byte[] hash = reader.ReadBytes(32);
                double entropy = reader.ReadDouble();
                int seen = reader.ReadInt32();
                if (hash.Length == 32)
                    into[path] = new Entry(ticks, size, hash, entropy, seen);
            }
        }

        private static void LoadLegacy(ConcurrentDictionary<string, Entry> into)
        {
            int today = Today;
            foreach (string line in File.ReadLines(LegacyCachePath))
            {
                var parts = line.Split('\t', 5);
                if (parts.Length != 5 || parts[2].Length != 64) continue;
                if (!long.TryParse(parts[0], out long ticks) || !long.TryParse(parts[1], out long size)) continue;
                if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double entropy)) entropy = -1;
                try { into[parts[4]] = new Entry(ticks, size, Convert.FromHexString(parts[2]), entropy, today); }
                catch (FormatException) { }
            }
        }

        public static Print? TryGet(string path, bool measureEntropy, bool useCache)
        {
            FileInfo info;
            try { info = new FileInfo(path); }
            catch { return null; }
            return TryGet(info, measureEntropy, useCache, null);
        }

        public static Print? TryGet(FileInfo info, bool measureEntropy, bool useCache, SemaphoreSlim? readGate)
        {
            long size, ticks;
            try
            {
                if (!info.Exists) return null;
                size = info.Length;
                ticks = info.LastWriteTimeUtc.Ticks;
            }
            catch { return null; }
            return TryGet(info.FullName, size, ticks, measureEntropy, useCache, readGate);
        }

        public static Print? TryGet(string path, long size, long ticks, bool measureEntropy, bool useCache, SemaphoreSlim? readGate, CancellationToken cancel = default)
        {
            bool wantEntropy = measureEntropy && size >= 2048;
            if (useCache && Cache.TryGetValue(path, out var hit) && hit.Size == size && hit.Ticks == ticks
                && (!wantEntropy || hit.Entropy >= 0))
            {
                int today = Today;
                if (hit.LastSeenDay != today)
                {
                    hit.LastSeenDay = today;
                    dirty = true;
                }
                return new Print(Convert.ToHexString(hit.Hash), size, wantEntropy ? hit.Entropy : -1, true);
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent((int)Math.Clamp(size, 4096, BufferSize));
            int[]? counts = wantEntropy ? ArrayPool<int>.Shared.Rent(1024) : null;
            bool gated = false;
            try
            {
                if (readGate != null)
                {
                    readGate.Wait(cancel);
                    gated = true;
                }
                if (counts != null)
                    Array.Clear(counts, 0, 1024);
                using var stream = ForceDelete.OpenRead(path, 0, useAsync: false);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long sampled = 0, total = 0;
                int chunk = Math.Min(buffer.Length, BufferSize);
                int read;
                while ((read = stream.Read(buffer, 0, chunk)) > 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    sha.AppendData(buffer, 0, read);
                    total += read;
                    if (counts != null && sampled < EntropySample)
                    {
                        int take = (int)Math.Min(read, EntropySample - sampled);
                        CountBytes(buffer.AsSpan(0, take), counts);
                        sampled += take;
                    }
                }

                double entropy = -1;
                if (counts != null && sampled > 0)
                {
                    entropy = 0;
                    for (int i = 0; i < 256; i++)
                    {
                        long count = (long)counts[i] + counts[256 + i] + counts[512 + i] + counts[768 + i];
                        if (count == 0) continue;
                        double p = (double)count / sampled;
                        entropy -= p * Math.Log2(p);
                    }
                }

                byte[] hash = sha.GetHashAndReset();
                if (useCache)
                {
                    var entry = new Entry(ticks, total, hash, entropy, Today);
                    if (Cache.ContainsKey(path))
                        Cache[path] = entry;
                    else if (Volatile.Read(ref entryCount) < MaxCacheEntries && Cache.TryAdd(path, entry))
                        Interlocked.Increment(ref entryCount);
                    dirty = true;
                }
                return new Print(Convert.ToHexString(hash), total, entropy, false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (gated)
                    readGate!.Release();
                ArrayPool<byte>.Shared.Return(buffer);
                if (counts != null)
                    ArrayPool<int>.Shared.Return(counts);
            }
        }

        private static void CountBytes(ReadOnlySpan<byte> data, int[] counts)
        {
            int i = 0;
            for (; i + 3 < data.Length; i += 4)
            {
                counts[data[i]]++;
                counts[256 + data[i + 1]]++;
                counts[512 + data[i + 2]]++;
                counts[768 + data[i + 3]]++;
            }
            for (; i < data.Length; i++)
                counts[data[i]]++;
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
                Directory.CreateDirectory(CacheFolder);
                int oldest = Today - KeepDays;
                var keep = cache.Where(kv => kv.Value.LastSeenDay >= oldest).Take(MaxCacheEntries).ToList();
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
                        writer.Write(kv.Value.Hash);
                        writer.Write(kv.Value.Entropy);
                        writer.Write(kv.Value.LastSeenDay);
                    }
                }
                File.Move(tmp, CachePath, overwrite: true);
                foreach (var kv in cache.Where(kv => kv.Value.LastSeenDay < oldest).ToList())
                    cache.TryRemove(kv.Key, out _);
                entryCount = cache.Count;
                try { File.Delete(LegacyCachePath); } catch { }
                dirty = false;
            }
            catch { }
        }

        public static bool IsEmpty =>
            (cache == null || cache.IsEmpty) && !File.Exists(CachePath) && !File.Exists(LegacyCachePath) && HeaderCache.IsEmpty;

        public static void Clear()
        {
            cache?.Clear();
            entryCount = 0;
            dirty = false;
            try { File.Delete(CachePath); } catch { }
            try { File.Delete(LegacyCachePath); } catch { }
            HeaderCache.Clear();
        }
    }
}
