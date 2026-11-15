using System;
using System.Runtime.InteropServices;

namespace MultronWinCleaner.Processes
{
    public static class RamUsage
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

        public static (ulong Total, ulong Used, double Percent) Read()
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0)
            {
                var info = new Microsoft.VisualBasic.Devices.ComputerInfo();
                ulong total = info.TotalPhysicalMemory;
                ulong used = total - info.AvailablePhysicalMemory;
                return (total, used, total == 0 ? 0 : used * 100.0 / total);
            }
            ulong usedPhys = status.TotalPhys - status.AvailPhys;
            return (status.TotalPhys, usedPhys, usedPhys * 100.0 / status.TotalPhys);
        }
    }
}
