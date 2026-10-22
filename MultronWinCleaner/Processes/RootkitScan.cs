using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace MultronWinCleaner.Processes
{
    public static class RootkitScan
    {
        public sealed class HiddenProcess
        {
            public int Pid;
            public string ImagePath = "";
            public string Name = "";
        }

        private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const int MaxProbedPid = 100000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, System.Text.StringBuilder name, ref int size);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        public static List<HiddenProcess> FindHiddenProcesses(CancellationToken ct)
        {
            var hidden = new List<HiddenProcess>();

            var visible = SnapshotVisiblePids();
            for (int pid = 8; pid <= MaxProbedPid; pid += 4)
            {
                ct.ThrowIfCancellationRequested();
                if (visible.Contains(pid))
                    continue;

                IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (handle == IntPtr.Zero)
                    continue;
                try
                {
                    string? image = ImagePath(handle);
                    if (string.IsNullOrEmpty(image))
                        continue;
                    hidden.Add(new HiddenProcess { Pid = pid, ImagePath = image!, Name = Path.GetFileName(image!) });
                }
                finally
                {
                    CloseHandle(handle);
                }
            }

            if (hidden.Count == 0)
                return hidden;

            var confirmed = new List<HiddenProcess>();
            var visibleAgain = SnapshotVisiblePids();
            foreach (var h in hidden)
            {
                if (visibleAgain.Contains(h.Pid))
                    continue;
                IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, h.Pid);
                if (handle == IntPtr.Zero)
                    continue;
                CloseHandle(handle);
                confirmed.Add(h);
            }
            return confirmed;
        }

        private static HashSet<int> SnapshotVisiblePids()
        {
            var set = new HashSet<int>();
            foreach (Process p in Process.GetProcesses())
            {
                try { set.Add(p.Id); }
                catch { }
                finally { p.Dispose(); }
            }
            return set;
        }

        private static string? ImagePath(IntPtr handle)
        {
            try
            {
                var name = new System.Text.StringBuilder(1024);
                int size = name.Capacity;
                return QueryFullProcessImageName(handle, 0, name, ref size) ? name.ToString() : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
