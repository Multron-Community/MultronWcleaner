using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MultronWinCleaner.Processes
{
    public enum ScanPriority
    {
        Low,
        Normal,
        High
    }

    public enum ScanDiskMode
    {
        Automatic,
        SolidState,
        HardDisk
    }

    public sealed class ScanPriorityScope : IDisposable
    {
        private const uint PROCESS_MODE_BACKGROUND_BEGIN = 0x00100000;
        private const uint PROCESS_MODE_BACKGROUND_END = 0x00200000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetPriorityClass(IntPtr hProcess, uint dwPriorityClass);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        private readonly ScanPriority priority;
        private readonly ProcessPriorityClass? previous;
        private bool background;

        public ScanPriorityScope(ScanPriority priority)
        {
            this.priority = priority;
            try
            {
                if (priority == ScanPriority.Low)
                    background = SetPriorityClass(GetCurrentProcess(), PROCESS_MODE_BACKGROUND_BEGIN);
                else if (priority == ScanPriority.High)
                {
                    using var process = Process.GetCurrentProcess();
                    previous = process.PriorityClass;
                    if (previous == ProcessPriorityClass.Normal)
                        process.PriorityClass = ProcessPriorityClass.AboveNormal;
                    else
                        previous = null;
                }
            }
            catch { }
        }

        public void Dispose()
        {
            try
            {
                if (background)
                {
                    SetPriorityClass(GetCurrentProcess(), PROCESS_MODE_BACKGROUND_END);
                    background = false;
                }
                else if (priority == ScanPriority.High && previous is ProcessPriorityClass value)
                {
                    using var process = Process.GetCurrentProcess();
                    process.PriorityClass = value;
                }
            }
            catch { }
        }
    }
}
