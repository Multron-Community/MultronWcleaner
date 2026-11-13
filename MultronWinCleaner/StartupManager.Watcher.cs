using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MultronWinCleaner
{
    public partial class StartupManager
    {
        private const int NotifyNameAndValue = 1 | 4;
        private const string ScheduledTasksKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache\Tree";

        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(200);

        private readonly SemaphoreSlim _changeSignal = new SemaphoreSlim(0, 1);
        private volatile bool _taskListChanged;

        [DllImport("advapi32.dll")]
        private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key, bool watchSubtree, int filter, SafeWaitHandle changedEvent, bool asynchronous);

        private sealed record KeyWatch(RegistryKey Key, bool IsTaskList, AutoResetEvent Changed);

        private async Task WaitForChangeAsync(CancellationToken token)
        {
            if (await _changeSignal.WaitAsync(PollInterval, token))
                await Task.Delay(SettleDelay, token);
        }

        private void SignalChange()
        {
            try { _changeSignal.Release(); }
            catch (SemaphoreFullException) { }
        }

        private void StartChangeWatchers(CancellationToken token)
        {
            new Thread(() => WatchRegistry(token)) { IsBackground = true }.Start();

            foreach (var special in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
            {
                try
                {
                    string folder = Environment.GetFolderPath(special);
                    if (!Directory.Exists(folder))
                        continue;
                    var watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName };
                    watcher.Created += (s, e) => SignalChange();
                    watcher.Renamed += (s, e) => SignalChange();
                    watcher.Deleted += (s, e) => SignalChange();
                    watcher.EnableRaisingEvents = true;
                    token.Register(watcher.Dispose);
                }
                catch { }
            }
        }

        private static KeyWatch? OpenWatch(RegistryKey root, string path, bool isTaskList)
        {
            try
            {
                var key = root.OpenSubKey(path, false);
                return key == null ? null : new KeyWatch(key, isTaskList, new AutoResetEvent(false));
            }
            catch
            {
                return null;
            }
        }

        private static void Arm(KeyWatch watch) =>
            RegNotifyChangeKeyValue(watch.Key.Handle, watch.IsTaskList, NotifyNameAndValue, watch.Changed.SafeWaitHandle, true);

        private void WatchRegistry(CancellationToken token)
        {
            string[] runKeys =
            {
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
                @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
                @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce"
            };

            var watches = new List<KeyWatch?>();
            foreach (string path in runKeys)
            {
                watches.Add(OpenWatch(Registry.CurrentUser, path, false));
                watches.Add(OpenWatch(Registry.LocalMachine, path, false));
            }
            watches.Add(OpenWatch(Registry.LocalMachine, ScheduledTasksKey, true));

            var active = watches.OfType<KeyWatch>().ToList();
            var handles = new WaitHandle[active.Count + 1];
            handles[0] = token.WaitHandle;
            for (int i = 0; i < active.Count; i++)
            {
                handles[i + 1] = active[i].Changed;
                Arm(active[i]);
            }

            try
            {
                while (true)
                {
                    int signaled = WaitHandle.WaitAny(handles);
                    if (signaled == 0)
                        break;
                    var watch = active[signaled - 1];
                    Arm(watch);
                    if (watch.IsTaskList)
                        _taskListChanged = true;
                    SignalChange();
                }
            }
            finally
            {
                foreach (var watch in active)
                {
                    watch.Key.Dispose();
                    watch.Changed.Dispose();
                }
            }
        }
    }
}
