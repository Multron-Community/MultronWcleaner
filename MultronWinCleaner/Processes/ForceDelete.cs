using MFK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MultronWinCleaner.Processes
{
    public static class ForceDelete
    {
        public enum Outcome { Deleted, ScheduledForReboot, Failed }

        public sealed class Result
        {
            public Outcome Outcome;
            public List<string> Steps = new List<string>();
            public string Error = "";
            public Action<string>? Progress;
        }

        private static void Step(Result result, string text)
        {
            result.Steps.Add(text);
            result.Progress?.Invoke(text);
        }

        private static void Report(Result result, string text) => result.Progress?.Invoke(text);

        private const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, int dwFlags);

        public static Result Delete(string path, Action<string>? progress = null)
        {
            EnsureBackupPrivileges();
            var result = new Result { Progress = progress };
            path = Path.GetFullPath(path);

            if (File.Exists(path))
            {
                Report(result, Loc.T("Looking for programs started from this file..."));
                KillProcessesStartedFrom(path, result);
            }
            Report(result, Loc.T("Deleting the file..."));
            if (TryDelete(path, result))
                return Done(result, Outcome.Deleted);
            Report(result, Loc.F("Delete failed: {0}", result.Error));

            if (ClearAttributes(path))
                Step(result, Loc.T("Removed read-only, hidden and system attributes"));
            if (TryDelete(path, result))
                return Done(result, Outcome.Deleted);

            Report(result, Loc.T("Unlinking the file directly..."));
            if (ForcePosixDelete(path, result) && !File.Exists(path))
                return Done(result, Outcome.Deleted);

            Report(result, Loc.T("Looking for programs that lock the file..."));
            if (CloseLockingProcesses(path, result) && (TryDeleteWithRetries(path, result) || ForcePosixDelete(path, result)) && !File.Exists(path))
                return Done(result, Outcome.Deleted);

            Report(result, Loc.T("Taking ownership of the file..."));
            if (TakeOwnership(path, result))
            {
                if (TryDeleteWithRetries(path, result))
                    return Done(result, Outcome.Deleted);
                if (ForcePosixDelete(path, result) && !File.Exists(path))
                    return Done(result, Outcome.Deleted);
            }

            Report(result, Loc.T("Attempting deletion with SYSTEM privileges via Sysinternals PsExec..."));
            if (DeleteAsSystemWithPsExec(path, result) && !File.Exists(path))
                return Done(result, Outcome.Deleted);

            string target = path;
            try
            {
                string renamed = Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path) + ".mwc_del.tmp");
                File.Move(path, renamed);
                target = renamed;
                Step(result, Loc.T("Renamed the file so it can no longer start by its original name"));
                if (TryDelete(target, result))
                    return Done(result, Outcome.Deleted);

                if (DeleteAsSystemWithPsExec(target, result) && !File.Exists(target))
                    return Done(result, Outcome.Deleted);
            }
            catch (Exception) { }

            Report(result, Loc.T("Scheduling the file for deletion at the next restart..."));
            if (ScheduleAtReboot(target, null, result))
                return Done(result, Outcome.ScheduledForReboot);

            result.Error = string.IsNullOrEmpty(result.Error) ? new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message : result.Error;
            if (result.Error.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0)
                Step(result, Loc.T("This file is protected by active security software (self-protection). Turn off that program's self-protection and close it, or restart Windows in Safe Mode, then remove the file again."));
            return Done(result, Outcome.Failed);
        }

        public static Result Move(string path, string destination, Action<string>? progress = null)
        {
            var result = new Result { Progress = progress };
            path = Path.GetFullPath(path);

            bool TryMove()
            {
                try
                {
                    File.Move(path, destination);
                    return true;
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                    return false;
                }
            }

            bool TryMoveWithRetries()
            {
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    if (TryMove())
                        return true;
                    Thread.Sleep(300);
                }
                return false;
            }

            if (File.Exists(path))
            {
                Report(result, Loc.T("Looking for programs started from this file..."));
                KillProcessesStartedFrom(path, result);
            }
            Report(result, Loc.T("Moving the file to quarantine..."));
            if (TryMove())
                return Done(result, Outcome.Deleted);
            Report(result, Loc.F("Move failed: {0}", result.Error));

            if (ClearAttributes(path))
                Step(result, Loc.T("Removed read-only, hidden and system attributes"));
            if (TryMove())
                return Done(result, Outcome.Deleted);

            Report(result, Loc.T("Looking for programs that lock the file..."));
            if (CloseLockingProcesses(path, result) && TryMoveWithRetries())
                return Done(result, Outcome.Deleted);

            Report(result, Loc.T("Taking ownership of the file..."));
            if (TakeOwnership(path, result) && TryMoveWithRetries())
                return Done(result, Outcome.Deleted);

            Report(result, Loc.T("Attempting move with SYSTEM privileges via Sysinternals PsExec..."));
            if (MoveAsSystemWithPsExec(path, destination, result))
                return Done(result, Outcome.Deleted);

            Report(result, Loc.T("Scheduling the file to be moved to quarantine at the next restart..."));
            if (ScheduleAtReboot(path, destination, result))
                return Done(result, Outcome.ScheduledForReboot);

            result.Error = string.IsNullOrEmpty(result.Error) ? new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message : result.Error;
            if (result.Error.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0)
                Step(result, Loc.T("This file is protected by active security software (self-protection). Turn off that program's self-protection and close it, or restart Windows in Safe Mode, then remove the file again."));
            return Done(result, Outcome.Failed);
        }

        private static Result Done(Result result, Outcome outcome)
        {
            result.Outcome = outcome;
            return result;
        }

        private static bool TryDelete(string path, Result result)
        {
            try
            {
                if (!File.Exists(path))
                    return true;
                File.Delete(path);
                if (!File.Exists(path))
                    return true;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            try
            {
                if (DeleteFileW(ExtendedPath(path)) && !File.Exists(path))
                    return true;
                int err = Marshal.GetLastWin32Error();
                if (err != 0)
                    result.Error = new System.ComponentModel.Win32Exception(err).Message;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            return !File.Exists(path);
        }

        private static bool TryDeleteWithRetries(string path, Result result)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (TryDelete(path, result))
                    return true;
                Thread.Sleep(300);
            }
            return false;
        }

        public static bool TryHardDelete(string path)
        {
            try { path = Path.GetFullPath(path); } catch { return false; }
            EnsureBackupPrivileges();
            var result = new Result();
            if (TryDelete(path, result))
                return true;
            if (ClearAttributes(path) && TryDelete(path, result))
                return true;
            if (ForcePosixDelete(path, result) && !File.Exists(path))
                return true;

            if (TakeOwnership(path, result))
            {
                if (TryDelete(path, result))
                    return true;
                if (ForcePosixDelete(path, result) && !File.Exists(path))
                    return true;
            }
            return !File.Exists(path);
        }


        private static readonly Lazy<bool> BackupPrivileges = new(EnableBackupPrivileges);

        internal static void EnsureBackupPrivileges() => _ = BackupPrivileges.Value;

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES { public int PrivilegeCount; public LUID Luid; public uint Attributes; }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValueW(string? systemName, string name, out LUID luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAll, ref TOKEN_PRIVILEGES newState,
            int bufferLength, IntPtr previousState, IntPtr returnLength);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        private static bool EnableBackupPrivileges()
        {
            const uint TOKEN_ADJUST_PRIVILEGES = 0x20, TOKEN_QUERY = 0x8, SE_PRIVILEGE_ENABLED = 0x2;
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr token))
                return false;
            bool any = false;
            try
            {
                foreach (string name in new[] { "SeBackupPrivilege", "SeRestorePrivilege", "SeTakeOwnershipPrivilege" })
                {
                    if (!LookupPrivilegeValueW(null, name, out LUID luid))
                        continue;
                    var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
                    if (AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero) && Marshal.GetLastWin32Error() == 0)
                        any = true;
                }
            }
            catch { }
            finally { CloseHandle(token); }
            return any;
        }

        internal static Microsoft.Win32.SafeHandles.SafeFileHandle? OpenForOverwrite(string path, bool writeThrough)
        {
            EnsureBackupPrivileges();
            const int GENERIC_WRITE = 0x40000000;
            const int FILE_FLAG_WRITE_THROUGH = unchecked((int)0x80000000);
            IntPtr h = CreateFileW(ExtendedPath(path), GENERIC_WRITE, 0, IntPtr.Zero, OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT | (writeThrough ? FILE_FLAG_WRITE_THROUGH : 0), IntPtr.Zero);
            if (h == InvalidHandle || h == IntPtr.Zero)
                return null;
            return new Microsoft.Win32.SafeHandles.SafeFileHandle(h, ownsHandle: true);
        }

        internal static FileStream? OpenForBackupRead(string path, int bufferSize, bool useAsync)
        {
            EnsureBackupPrivileges();
            const int GENERIC_READ = unchecked((int)0x80000000);
            const int FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000;
            const int FILE_FLAG_OVERLAPPED = 0x40000000;
            IntPtr h = CreateFileW(ExtendedPath(path), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, IntPtr.Zero, OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_SEQUENTIAL_SCAN | (useAsync ? FILE_FLAG_OVERLAPPED : 0), IntPtr.Zero);
            if (h == InvalidHandle || h == IntPtr.Zero)
                return null;
            var handle = new Microsoft.Win32.SafeHandles.SafeFileHandle(h, ownsHandle: true);
            return new FileStream(handle, FileAccess.Read, bufferSize, useAsync);
        }

        internal static FileStream OpenRead(string path, int bufferSize, bool useAsync)
        {
            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize,
                    useAsync ? FileOptions.Asynchronous | FileOptions.SequentialScan : FileOptions.SequentialScan);
            }
            catch (UnauthorizedAccessException)
            {
                var stream = OpenForBackupRead(path, bufferSize, useAsync);
                if (stream == null)
                    throw;
                return stream;
            }
        }

        [ThreadStatic]
        private static int lastScheduleError;

        public static int LastScheduleError => lastScheduleError;

        public static bool ScheduleDeleteAtReboot(string path)
        {
            lastScheduleError = 0;
            try { path = Path.GetFullPath(path); } catch { return false; }
            if (!File.Exists(path))
                return true;
            if (IsScheduledForReboot(path))
                return true;
            if (MoveFileEx(path, null, MOVEFILE_DELAY_UNTIL_REBOOT))
                return true;
            lastScheduleError = Marshal.GetLastWin32Error();
            return TryAppendPendingRename(path, null);
        }

        private const int DELETE = 0x00010000;
        private const int FILE_SHARE_READ = 0x1;
        private const int FILE_SHARE_WRITE = 0x2;
        private const int FILE_SHARE_DELETE = 0x4;
        private const int OPEN_EXISTING = 3;
        private const int FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private const int FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;

        private const int FileDispositionInfo = 4;
        private const int FileDispositionInfoEx = 21;

        private const int FILE_DISPOSITION_FLAG_DELETE = 0x1;
        private const int FILE_DISPOSITION_FLAG_POSIX_SEMANTICS = 0x2;
        private const int FILE_DISPOSITION_FLAG_IGNORE_READONLY_ATTRIBUTE = 0x10;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool DeleteFileW(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileW(string lpFileName, int dwDesiredAccess, int dwShareMode,
            IntPtr lpSecurityAttributes, int dwCreationDisposition, int dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFileInformationByHandle(IntPtr hFile, int fileInformationClass, ref int fileInformation, int dwBufferSize);

        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        internal static string ExtendedPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\??\", StringComparison.Ordinal))
                return path;
            if (path.StartsWith(@"\\", StringComparison.Ordinal))
                return @"\\?\UNC\" + path.Substring(2);
            return @"\\?\" + path;
        }

        private static bool ForcePosixDelete(string path, Result result)
        {
            if (!File.Exists(path))
                return true;

            IntPtr handle = CreateFileW(ExtendedPath(path), DELETE,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, IntPtr.Zero, OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
            if (handle == InvalidHandle || handle == IntPtr.Zero)
                return false;

            try
            {
                int flagsEx = FILE_DISPOSITION_FLAG_DELETE | FILE_DISPOSITION_FLAG_POSIX_SEMANTICS | FILE_DISPOSITION_FLAG_IGNORE_READONLY_ATTRIBUTE;
                if (SetFileInformationByHandle(handle, FileDispositionInfoEx, ref flagsEx, sizeof(int)))
                {
                    Step(result, Loc.T("Unlinked the file with NTFS POSIX semantics"));
                    return true;
                }

                int flag = 1;
                if (SetFileInformationByHandle(handle, FileDispositionInfo, ref flag, sizeof(int)))
                {
                    Step(result, Loc.T("Marked the file for deletion when it is released"));
                    return true;
                }
            }
            catch (Exception) { }
            finally
            {
                CloseHandle(handle);
            }
            return !File.Exists(path);
        }

        private static bool ClearAttributes(string path)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                FileAttributes cleared = attributes & ~(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System);
                if (cleared == attributes)
                    return false;
                File.SetAttributes(path, cleared == 0 ? FileAttributes.Normal : cleared);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void KillProcessesStartedFrom(string path, Result result)
        {
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    int id;
                    string name;
                    try
                    {
                        id = process.Id;
                        name = process.ProcessName;
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    if (!string.Equals(ImagePath(id), path, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (WhoUsesFile.IsProtectedProcess(id, name))
                    {
                        Step(result, Loc.F("Skipped Windows process {0} (PID {1})", name, id));
                        continue;
                    }

                    try
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(5000);
                        Step(result, Loc.F("Force-closed {0} (PID {1}) and the programs it started", name, id));
                    }
                    catch (Exception ex)
                    {
                        Step(result, Loc.F("Could not close {0} (PID {1}): {2}", name, id, ex.Message));
                    }
                }
            }
        }

        private static bool CloseLockingProcesses(string path, Result result)
        {
            var targets = new Dictionary<int, string>();
            foreach (var locker in WhoUsesFile.GetLockers(path))
                targets[locker.Id] = locker.Name;

            string processName = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(processName))
            {
                foreach (Process process in Process.GetProcessesByName(processName))
                {
                    using (process)
                    {
                        try
                        {
                            if (!targets.ContainsKey(process.Id) && string.Equals(ImagePath(process.Id), path, StringComparison.OrdinalIgnoreCase))
                                targets[process.Id] = process.ProcessName;
                        }
                        catch (Exception) { }
                    }
                }
            }

            bool closedAny = false;
            foreach (var target in targets)
            {
                if (WhoUsesFile.IsProtectedProcess(target.Key, target.Value))
                {
                    Step(result, Loc.F("Skipped Windows process {0} (PID {1})", target.Value, target.Key));
                    continue;
                }

                try
                {
                    using (Process process = Process.GetProcessById(target.Key))
                    {
                        process.CloseMainWindow();
                        if (!process.WaitForExit(5000))
                        {
                            process.Kill();
                            process.WaitForExit(5000);
                        }
                    }
                    Step(result, Loc.F("Closed {0} (PID {1})", target.Value, target.Key));
                    closedAny = true;
                }
                catch (ArgumentException)
                {
                    closedAny = true;
                }
                catch (Exception ex)
                {
                    Step(result, Loc.F("Could not close {0} (PID {1}): {2}", target.Value, target.Key, ex.Message));
                }
            }
            return closedAny;
        }

        private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, System.Text.StringBuilder name, ref int size);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        private static string? ImagePath(int processId)
        {
            IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (handle == IntPtr.Zero)
                return null;
            try
            {
                var name = new System.Text.StringBuilder(1024);
                int size = name.Capacity;
                return QueryFullProcessImageName(handle, 0, name, ref size) ? Path.GetFullPath(name.ToString()) : null;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static bool TakeOwnership(string path, Result result)
        {
            bool owned = RunTool("takeown.exe", $"/F \"{path}\" /A");
            RunTool("icacls.exe", $"\"{path}\" /reset /C");
            RunTool("icacls.exe", $"\"{path}\" /remove:d *S-1-1-0 *S-1-5-32-544 *S-1-5-18 /C");
            RunTool("icacls.exe", $"\"{path}\" /inheritance:e /C");
            bool granted = RunTool("icacls.exe", $"\"{path}\" /grant *S-1-5-32-544:F *S-1-5-18:F /C");
            if (owned || granted)
                Step(result, Loc.T("Took ownership, reset permissions and gave Administrators full control"));
            return owned || granted;
        }

        private static bool RunTool(string fileName, string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, fileName),
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (Process? process = Process.Start(startInfo))
                {
                    if (process == null)
                        return false;
                    process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(15000))
                    {
                        process.Kill();
                        return false;
                    }
                    return process.ExitCode == 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void EnsurePsExecEulaAccepted()
        {
            try
            {
                using var cuKey = Registry.CurrentUser.CreateSubKey(@"Software\Sysinternals\PsExec");
                cuKey?.SetValue("EulaAccepted", 1, RegistryValueKind.DWord);
            }
            catch { }

            try
            {
                using var lmKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Sysinternals\PsExec");
                lmKey?.SetValue("EulaAccepted", 1, RegistryValueKind.DWord);
            }
            catch { }
        }

        private const string PsExecResourceName = "MultronWinCleaner.Tools.PsExec64.exe";
        private static string? _extractedPsExecPath;
        private static readonly object PsExecExtractLock = new object();

        private static string? ExtractEmbeddedPsExec()
        {
            if (!string.IsNullOrEmpty(_extractedPsExecPath) && File.Exists(_extractedPsExecPath))
                return _extractedPsExecPath;

            lock (PsExecExtractLock)
            {
                if (!string.IsNullOrEmpty(_extractedPsExecPath) && File.Exists(_extractedPsExecPath))
                    return _extractedPsExecPath;

                try
                {
                    var assembly = typeof(ForceDelete).Assembly;
                    using Stream? stream = assembly.GetManifestResourceStream(PsExecResourceName);
                    if (stream == null)
                        return null;

                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MultronWinCleaner", "Tools");
                    Directory.CreateDirectory(dir);
                    string target = Path.Combine(dir, "PsExec64.exe");

                    bool needsWrite = true;
                    if (File.Exists(target))
                    {
                        try { needsWrite = new FileInfo(target).Length != stream.Length; }
                        catch { needsWrite = true; }
                    }

                    if (needsWrite)
                    {
                        using var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                        stream.CopyTo(file);
                    }

                    _extractedPsExecPath = target;
                    return target;
                }
                catch
                {
                    return null;
                }
            }
        }

        private static string? FindPsExecPath()
        {
            string? embedded = ExtractEmbeddedPsExec();
            if (!string.IsNullOrEmpty(embedded))
                return embedded;

            bool is64 = Environment.Is64BitOperatingSystem;
            string primary = is64 ? "PsExec64.exe" : "PsExec.exe";
            string secondary = is64 ? "PsExec.exe" : "PsExec64.exe";

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string appBase = AppDomain.CurrentDomain.BaseDirectory;

            var candidates = new List<string>
            {
                appBase,
                Path.Combine(appBase, "PSTools"),
                Path.Combine(appBase, "Tools"),
                Path.Combine(userProfile, "Downloads", "Compressed", "PSTools"),
                Path.Combine(userProfile, "Downloads", "PSTools")
            };

            foreach (var dir in candidates)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    continue;

                string p1 = Path.Combine(dir, primary);
                if (File.Exists(p1)) return p1;

                string p2 = Path.Combine(dir, secondary);
                if (File.Exists(p2)) return p2;
            }

            try
            {
                string? pathEnv = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(pathEnv))
                {
                    foreach (string part in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string trimmed = part.Trim();
                        if (string.IsNullOrEmpty(trimmed) || !Directory.Exists(trimmed)) continue;

                        string p1 = Path.Combine(trimmed, primary);
                        if (File.Exists(p1)) return p1;

                        string p2 = Path.Combine(trimmed, secondary);
                        if (File.Exists(p2)) return p2;
                    }
                }
            }
            catch { }

            return null;
        }

        private readonly struct PsExecRun
        {
            public readonly bool Started;
            public readonly bool TimedOut;
            public readonly int ExitCode;
            public readonly string Output;

            public PsExecRun(bool started, bool timedOut, int exitCode, string output)
            {
                Started = started;
                TimedOut = timedOut;
                ExitCode = exitCode;
                Output = output;
            }
        }

        private static PsExecRun RunPsExec(string psExecPath, string arguments, int timeoutMs = 20000)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = psExecPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                    return new PsExecRun(false, false, -1, "");

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    return new PsExecRun(true, true, -1, "");
                }

                Task.WaitAll(new Task[] { stdoutTask, stderrTask }, 2000);
                string stdout = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : "";
                string stderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : "";
                return new PsExecRun(true, false, process.ExitCode, (stderr + " " + stdout).Trim());
            }
            catch (Exception ex)
            {
                return new PsExecRun(false, false, -1, ex.Message);
            }
        }

        private static readonly string[] PsExecNoiseMarkers =
        {
            "Connecting to local system", "Starting PSEXESVC service", "Copying authentication key",
            "Connecting with PsExec service", "Starting powershell on", "Starting cmd", "Starting cmd.exe on",
            "PsExec v", "Sysinternals - www.sysinternals.com", "Copyright (C)"
        };

        private static string SummarizePsExec(PsExecRun run)
        {
            if (!run.Started)
                return Loc.F("PsExec could not start: {0}", string.IsNullOrEmpty(run.Output) ? Loc.T("unknown error") : run.Output);
            if (run.TimedOut)
                return Loc.T("PsExec did not finish in time");

            var kept = new List<string>();
            foreach (string rawLine in run.Output.Split('\n'))
            {
                string line = rawLine.Replace('\r', ' ').Trim();
                if (line.Length == 0)
                    continue;
                bool noise = false;
                foreach (string marker in PsExecNoiseMarkers)
                    if (line.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        noise = true;
                        break;
                    }
                if (!noise)
                    kept.Add(line);
            }

            string text = string.Join(" ", kept);
            while (text.Contains("  "))
                text = text.Replace("  ", " ");
            text = text.Trim();
            if (text.Length > 400)
                text = text.Substring(0, 400) + "...";
            return string.IsNullOrEmpty(text) ? Loc.F("exit code {0}", run.ExitCode) : Loc.F("exit code {0}: {1}", run.ExitCode, text);
        }

        private const string SessionManagerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager";
        private const string PendingRenameValue = "PendingFileRenameOperations";

        private static string NativePath(string path) =>
            path.StartsWith(@"\??\", StringComparison.Ordinal) ? path : @"\??\" + path;

        private static bool IsScheduledForReboot(string path)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(SessionManagerKey);
                string native = NativePath(path);
                foreach (string valueName in new[] { PendingRenameValue, PendingRenameValue + "2" })
                {
                    if (key?.GetValue(valueName) is not string[] entries)
                        continue;
                    foreach (string entry in entries)
                        if (string.Equals(entry, native, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(entry, path, StringComparison.OrdinalIgnoreCase))
                            return true;
                }
            }
            catch { }
            return false;
        }

        private static readonly object PendingRenameLock = new();

        private static bool TryAppendPendingRename(string path, string? destination)
        {
            lock (PendingRenameLock)
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(SessionManagerKey, writable: true);
                if (key == null)
                    return false;
                var list = new List<string>();
                if (key.GetValue(PendingRenameValue) is string[] existing)
                    list.AddRange(existing);
                list.Add(NativePath(path));
                list.Add(string.IsNullOrEmpty(destination) ? "" : NativePath(destination));
                key.SetValue(PendingRenameValue, list.ToArray(), RegistryValueKind.MultiString);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ScheduleAtReboot(string path, string? destination, Result result)
        {
            bool move = !string.IsNullOrEmpty(destination);
            string okText = move
                ? Loc.T("Scheduled the file to be moved to quarantine at the next restart")
                : Loc.T("Scheduled the file for deletion at the next restart");
            string okSystemText = move
                ? Loc.T("Scheduled the file to be moved to quarantine at the next restart with SYSTEM privileges")
                : Loc.T("Scheduled the file for deletion at the next restart with SYSTEM privileges");

            if (IsScheduledForReboot(path))
            {
                Step(result, okText);
                return true;
            }

            if (MoveFileEx(path, destination, MOVEFILE_DELAY_UNTIL_REBOOT) && IsScheduledForReboot(path))
            {
                Step(result, okText);
                return true;
            }

            if (TryAppendPendingRename(path, destination) && IsScheduledForReboot(path))
            {
                Step(result, okText);
                return true;
            }

            string? psExec = FindPsExecPath();
            if (!string.IsNullOrEmpty(psExec))
            {
                EnsurePsExecEulaAccepted();
                string nativeSource = NativePath(path).Replace("'", "''");
                string nativeTarget = move ? NativePath(destination!).Replace("'", "''") : "";
                string script =
                    "$k='HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager';" +
                    "$v=(Get-ItemProperty -Path $k -Name PendingFileRenameOperations -ErrorAction SilentlyContinue).PendingFileRenameOperations;" +
                    "if($null -eq $v){$v=@()};" +
                    $"$v+=@('{nativeSource}','{nativeTarget}');" +
                    "Set-ItemProperty -Path $k -Name PendingFileRenameOperations -Value ([string[]]$v) -Type MultiString";
                PsExecRun run = RunPsExec(psExec, $"-s -accepteula -nobanner powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"");
                if (IsScheduledForReboot(path))
                {
                    Step(result, okSystemText);
                    return true;
                }
                Step(result, Loc.F("Could not schedule the operation at restart as SYSTEM ({0})", SummarizePsExec(run)));
            }

            return false;
        }

        private static bool DeleteAsSystemWithPsExec(string path, Result result)
        {
            string? psExec = FindPsExecPath();
            if (string.IsNullOrEmpty(psExec))
                return false;

            EnsurePsExecEulaAccepted();
            Report(result, Loc.T("Deleting with SYSTEM privileges using Sysinternals PsExec..."));

            PsExecRun run = RunPsExec(psExec, $"-s -accepteula -nobanner cmd.exe /c del /f /q /a \"{path}\"");
            if (!File.Exists(path))
            {
                Step(result, Loc.T("Deleted the file with SYSTEM privileges using Sysinternals PsExec"));
                return true;
            }

            run = RunPsExec(psExec, $"-s -accepteula -nobanner cmd.exe /c \"takeown /f \"{path}\" /a & icacls \"{path}\" /reset /c & icacls \"{path}\" /remove:d *S-1-1-0 *S-1-5-32-544 *S-1-5-18 /c & icacls \"{path}\" /grant *S-1-5-32-544:F /grant SYSTEM:F /c & attrib -r -s -h \"{path}\" & del /f /q /a \"{path}\"\"");
            if (!File.Exists(path))
            {
                Step(result, Loc.T("Reset permissions and deleted the file with SYSTEM privileges using Sysinternals PsExec"));
                return true;
            }

            run = RunPsExec(psExec, $"-accepteula -nobanner cmd.exe /c del /f /q /a \"{path}\"");
            if (!File.Exists(path))
            {
                Step(result, Loc.T("Deleted the file using Sysinternals PsExec"));
                return true;
            }

            Step(result, Loc.F("PsExec could not remove the file as SYSTEM ({0})", SummarizePsExec(run)));
            return false;
        }

        private static bool MoveAsSystemWithPsExec(string path, string destination, Result result)
        {
            string? psExec = FindPsExecPath();
            if (string.IsNullOrEmpty(psExec))
                return false;

            EnsurePsExecEulaAccepted();
            Report(result, Loc.T("Moving with SYSTEM privileges using Sysinternals PsExec..."));

            PsExecRun run = RunPsExec(psExec, $"-s -accepteula -nobanner cmd.exe /c move /y \"{path}\" \"{destination}\"");
            if (File.Exists(destination) && !File.Exists(path))
            {
                Step(result, Loc.T("Moved the file with SYSTEM privileges using Sysinternals PsExec"));
                return true;
            }

            run = RunPsExec(psExec, $"-accepteula -nobanner cmd.exe /c move /y \"{path}\" \"{destination}\"");
            if (File.Exists(destination) && !File.Exists(path))
            {
                Step(result, Loc.T("Moved the file using Sysinternals PsExec"));
                return true;
            }

            Step(result, Loc.F("PsExec could not move the file as SYSTEM ({0})", SummarizePsExec(run)));
            return false;
        }
    }
}
