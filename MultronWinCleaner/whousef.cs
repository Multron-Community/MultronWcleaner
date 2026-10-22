using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MFK
{
    using System.Runtime.InteropServices;
    using System.Diagnostics;
    using System;
    using System.Collections.Generic;

    public static class WhoUsesFile
    {
        [StructLayout(LayoutKind.Sequential)]
        struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        const int RmRebootReasonNone = 0;
        const int CCH_RM_MAX_APP_NAME = 255;
        const int CCH_RM_MAX_SVC_NAME = 63;

        enum RM_APP_TYPE
        {
            RmUnknownApp = 0,
            RmMainWindow = 1,
            RmOtherWindow = 2,
            RmService = 3,
            RmExplorer = 4,
            RmConsole = 5,
            RmCritical = 1000
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
            public string strAppName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
            public string strServiceShortName;

            public RM_APP_TYPE ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        static extern int RmRegisterResources(uint pSessionHandle,
                                              UInt32 nFiles,
                                              string[] rgsFilenames,
                                              UInt32 nApplications,
                                              [In] RM_UNIQUE_PROCESS[]? rgApplications,
                                              UInt32 nServices,
                                              string[]? rgsServiceNames);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Auto)]
        static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll")]
        static extern int RmGetList(uint dwSessionHandle,
                                    out uint pnProcInfoNeeded,
                                    ref uint pnProcInfo,
                                    [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
                                    ref uint lpdwRebootReasons);

        public static List<(int Id, string Name, bool IsService)> GetLockerDetails(string path)
        {
            var lockers = new List<(int Id, string Name, bool IsService)>();
            if (RmStartSession(out uint handle, 0, Guid.NewGuid().ToString()) != 0)
                return lockers;
            try
            {
                const int ERROR_MORE_DATA = 234;
                uint needed = 0, count = 0, reasons = RmRebootReasonNone;
                if (RmRegisterResources(handle, 1, new[] { path }, 0, null, 0, null) != 0)
                    return lockers;
                int res = RmGetList(handle, out needed, ref count, null, ref reasons);
                if (res != ERROR_MORE_DATA || needed == 0)
                    return lockers;
                var info = new RM_PROCESS_INFO[needed];
                count = needed;
                if (RmGetList(handle, out needed, ref count, info, ref reasons) != 0)
                    return lockers;
                for (int i = 0; i < count; i++)
                {
                    int id = info[i].Process.dwProcessId;
                    bool service = info[i].ApplicationType is RM_APP_TYPE.RmService or RM_APP_TYPE.RmCritical
                        || !string.IsNullOrEmpty(info[i].strServiceShortName);
                    string name = info[i].strAppName ?? "";
                    try
                    {
                        using var process = Process.GetProcessById(id);
                        name = process.ProcessName;
                    }
                    catch (Exception) { }
                    lockers.Add((id, name, service));
                }
            }
            catch (Exception) { }
            finally
            {
                RmEndSession(handle);
            }
            return lockers;
        }

        public static bool HasClosableLocker(string path) =>
            GetLockerDetails(path).Any(l => !l.IsService && !IsProtectedProcess(l.Id, l.Name));

        static public List<Process> WhoIsLocking(string path)
        {
            uint handle;
            string key = Guid.NewGuid().ToString();
            List<Process> processes = new List<Process>();

            int res = RmStartSession(out handle, 0, key);
            if (res != 0) throw new Exception(MultronWinCleaner.Loc.T("Could not begin restart session.  Unable to determine file locker."));

            try
            {
                const int ERROR_MORE_DATA = 234;
                uint pnProcInfoNeeded = 0,
                     pnProcInfo = 0,
                     lpdwRebootReasons = RmRebootReasonNone;

                string[] resources = new string[] { path };

                res = RmRegisterResources(handle, (uint)resources.Length, resources, 0, null, 0, null);

                if (res != 0) throw new Exception(MultronWinCleaner.Loc.T("Could not register resource."));

                res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, null, ref lpdwRebootReasons);

                if (res == ERROR_MORE_DATA)
                {
                    RM_PROCESS_INFO[] processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
                    pnProcInfo = pnProcInfoNeeded;

                    res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, ref lpdwRebootReasons);
                    if (res == 0)
                    {
                        processes = new List<Process>((int)pnProcInfo);

                        for (int i = 0; i < pnProcInfo; i++)
                        {
                            try
                            {
                                processes.Add(Process.GetProcessById(processInfo[i].Process.dwProcessId));
                            }
                            
                            catch (ArgumentException) { }
                        }
                    }
                    
                }
                
            }
            finally
            {
                RmEndSession(handle);
            }

            return processes;
        }

        public static List<(int Id, string Name)> GetLockers(string path)
        {
            var lockers = new List<(int Id, string Name)>();
            try
            {
                foreach (Process process in WhoIsLocking(path))
                {
                    using (process)
                    {
                        try
                        {
                            lockers.Add((process.Id, process.ProcessName));
                        }
                        catch (InvalidOperationException) { }
                    }
                }
            }
            catch (Exception) { }
            return lockers;
        }

        static readonly HashSet<string> CriticalProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "Registry", "Memory Compression", "smss", "csrss", "wininit", "winlogon", "services",
            "lsass", "lsaiso", "svchost", "fontdrvhost", "dwm", "explorer", "sihost", "taskhostw", "ctfmon",
            "spoolsv", "MsMpEng", "NisSrv", "SecurityHealthService", "audiodg", "conhost", "RuntimeBroker", "StartMenuExperienceHost",
            "TrustedInstaller", "TiWorker", "msiexec", "MoUsoCoreWorker", "usocoreworker", "wuauclt", "SearchIndexer", "SearchHost", "ShellExperienceHost"
        };

        public static bool IsProtectedProcess(int id, string name)
        {
            if (id <= 4 || id == Environment.ProcessId)
                return true;
            if (!CriticalProcessNames.Contains(name))
                return false;

            try
            {
                using (Process process = Process.GetProcessById(id))
                {
                    string? file = process.MainModule?.FileName;
                    string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    return string.IsNullOrEmpty(file) || file.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase)
                        || file.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "\\Windows Defender", StringComparison.OrdinalIgnoreCase)
                        || file.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + "\\Microsoft\\Windows Defender", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}
