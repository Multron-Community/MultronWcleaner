using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MultronWinCleaner.Processes
{
    public sealed class SmallestFirstQueue<T>
    {
        private readonly PriorityQueue<T, long> queue = new();
        private readonly SemaphoreSlim signal = new(0);
        private readonly object sync = new();
        private bool completed;

        public void Add(T item, long size)
        {
            lock (sync) queue.Enqueue(item, size);
            signal.Release();
        }

        public void Complete(int readers)
        {
            lock (sync)
            {
                if (completed) return;
                completed = true;
            }
            signal.Release(readers < 1 ? 1 : readers);
        }

        public async Task<(bool Ok, T Item)> TakeAsync(CancellationToken ct)
        {
            while (true)
            {
                lock (sync)
                {
                    if (queue.TryDequeue(out T? item, out _))
                        return (true, item);
                    if (completed)
                        return (false, default!);
                }
                await signal.WaitAsync(ct);
            }
        }
    }
}
