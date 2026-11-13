using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MultronWinCleaner.Processes
{
    public static class MemoryOptimizer
    {
        public sealed record Options(int Repeats, bool KeepForeground, bool SystemMemory, bool ReleaseCache, bool MergePages, ISet<string> Excluded);

        public readonly record struct Snapshot(long Available, long Free);

        private const int SystemMemoryListInformation = 80;
        private const int SystemCombinePhysicalMemoryInformation = 130;
        private const int MemoryEmptyWorkingSets = 2;
        private const int MemoryFlushModifiedList = 3;
        private const int MemoryPurgeStandbyList = 4;
        private const int MemoryPurgeLowPriorityStandbyList = 5;
        private const uint ProcessSetQuota = 0x0100;
        private const uint ProcessQueryInformation = 0x0400;
        private const uint TokenAdjustPrivileges = 0x0020;
        private const uint TokenQuery = 0x0008;
        private const uint SePrivilegeEnabled = 0x00000002;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct TokenPrivileges
        {
            public uint Count;
            public long Luid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryCombineInformationEx
        {
            public IntPtr Handle;
            public UIntPtr PagesCombined;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PerformanceInformation
        {
            public uint Size;
            public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
            public uint HandleCount, ProcessCount, ThreadCount;
        }

        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(int infoClass, ref int info, int length);

        [DllImport("ntdll.dll", EntryPoint = "NtSetSystemInformation")]
        private static extern int NtCombineMemory(int infoClass, ref MemoryCombineInformationEx info, int length);

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(int infoClass, [Out] long[] buffer, int length, out int returned);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetPerformanceInfo(out PerformanceInformation info, int size);

        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetSystemFileCacheSize(IntPtr minimum, IntPtr maximum, int flags);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges state, int length, IntPtr previous, IntPtr returned);

        [DllImport("dnsapi.dll")]
        private static extern uint DnsFlushResolverCache();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

        private static void EnablePrivilege(string name)
        {
            using var current = Process.GetCurrentProcess();
            if (!OpenProcessToken(current.Handle, TokenAdjustPrivileges | TokenQuery, out IntPtr token))
                return;
            try
            {
                if (!LookupPrivilegeValue(null, name, out long luid))
                    return;
                var state = new TokenPrivileges { Count = 1, Luid = luid, Attributes = SePrivilegeEnabled };
                AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                CloseHandle(token);
            }
        }

        private static void PreparePrivileges()
        {
            EnablePrivilege("SeProfileSingleProcessPrivilege");
            EnablePrivilege("SeIncreaseQuotaPrivilege");
        }

        private static bool MemoryCommand(int command)
        {
            int value = command;
            return NtSetSystemInformation(SystemMemoryListInformation, ref value, sizeof(int)) == 0;
        }

        private static void TrimProcesses(bool keepForeground, ISet<string> excluded)
        {
            int foreground = 0;
            if (keepForeground)
                GetWindowThreadProcessId(GetForegroundWindow(), out foreground);

            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (process.Id <= 4 || process.Id == foreground || excluded.Contains(process.ProcessName))
                        continue;
                    IntPtr handle = OpenProcess(ProcessSetQuota | ProcessQueryInformation, false, process.Id);
                    if (handle == IntPtr.Zero)
                        continue;
                    try { EmptyWorkingSet(handle); }
                    finally { CloseHandle(handle); }
                }
                catch { }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static void CombineIdenticalPages()
        {
            try
            {
                var info = new MemoryCombineInformationEx();
                NtCombineMemory(SystemCombinePhysicalMemoryInformation, ref info, Marshal.SizeOf<MemoryCombineInformationEx>());
            }
            catch { }
        }

        public static void Clean(Options options)
        {
            PreparePrivileges();
            bool trimEverything = !options.KeepForeground && options.Excluded.Count == 0;

            for (int pass = 0; pass < Math.Max(1, options.Repeats); pass++)
            {
                if (pass > 0)
                    System.Threading.Thread.Sleep(300);

                TrimProcesses(options.KeepForeground, options.Excluded);

                if (options.SystemMemory)
                {
                    if (trimEverything)
                        MemoryCommand(MemoryEmptyWorkingSets);
                    SetSystemFileCacheSize((IntPtr)(-1), (IntPtr)(-1), 0);
                }

                MemoryCommand(MemoryFlushModifiedList);

                if (options.SystemMemory)
                    MemoryCommand(MemoryPurgeLowPriorityStandbyList);

                if (options.MergePages)
                    CombineIdenticalPages();
            }

            if (options.ReleaseCache)
                MemoryCommand(MemoryPurgeStandbyList);
        }

        public static void FlushDnsCache()
        {
            try { DnsFlushResolverCache(); } catch { }
        }

        public static int UsedPercent()
        {
            if (!GetPerformanceInfo(out PerformanceInformation info, Marshal.SizeOf<PerformanceInformation>()) || (ulong)info.PhysicalTotal == 0)
                return 0;
            return (int)(100 - (ulong)info.PhysicalAvailable * 100 / (ulong)info.PhysicalTotal);
        }

        public static Snapshot Read()
        {
            PreparePrivileges();
            long pageSize = 4096, available = 0, free = 0;
            if (GetPerformanceInfo(out PerformanceInformation info, Marshal.SizeOf<PerformanceInformation>()))
            {
                pageSize = (long)info.PageSize;
                available = (long)info.PhysicalAvailable * pageSize;
            }
            var lists = new long[22];
            if (NtQuerySystemInformation(SystemMemoryListInformation, lists, sizeof(long) * lists.Length, out _) == 0)
                free = (lists[0] + lists[1]) * pageSize;
            return new Snapshot(available, free);
        }

        public static long Freed(Snapshot before, Snapshot after) =>
            Math.Max(0, Math.Max(after.Available - before.Available, after.Free - before.Free));
    }
}
