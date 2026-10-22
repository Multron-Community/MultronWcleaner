using System;
using System.IO;

namespace MultronWinCleaner.Processes
{
    public static class FileEntropy
    {
        public const double PackedThreshold = 7.2;

        private const int SampleBytes = 1 * 1024 * 1024;

        public readonly struct Result
        {
            public readonly double Entropy;
            public readonly long BytesRead;
            public readonly bool LikelyPacked;
            public Result(double entropy, long bytesRead)
            {
                Entropy = entropy;
                BytesRead = bytesRead;
                LikelyPacked = entropy >= PackedThreshold;
            }
        }

        public static Result Measure(string path)
        {
            var counts = new long[256];
            long total = 0;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16))
            {
                byte[] buffer = new byte[1 << 16];
                int read;
                while (total < SampleBytes && (read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < read; i++)
                        counts[buffer[i]]++;
                    total += read;
                }
            }

            if (total == 0)
                return new Result(0, 0);

            double entropy = 0;
            for (int i = 0; i < 256; i++)
            {
                if (counts[i] == 0) continue;
                double p = (double)counts[i] / total;
                entropy -= p * Math.Log2(p);
            }
            return new Result(entropy, total);
        }

        public static Result? TryMeasure(string path)
        {
            try { return Measure(path); }
            catch { return null; }
        }
    }
}
