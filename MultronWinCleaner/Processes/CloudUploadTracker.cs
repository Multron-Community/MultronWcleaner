using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace MultronWinCleaner.Processes
{
    public enum UploadStage { Waiting, Uploading, Analyzing, Done }

    [Flags]
    public enum ActivityFilter { None = 0, Uploads = 1, Threats = 2, Skipped = 4, Errors = 8, All = Uploads | Threats | Skipped | Errors }

    public sealed class CloudUploadItem : INotifyPropertyChanged
    {
        private readonly CancellationTokenSource cancel = new();
        private readonly Stopwatch timer = new();
        private UploadStage stage;
        private double progress;
        private long payloadBytes;
        private bool cancelRequested;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string FilePath { get; init; } = "";
        public string FileName => Path.GetFileName(FilePath);
        public long Size { get; init; }
        public bool IsUpload { get; set; }
        public bool BytesSent { get; private set; }

        public bool Matches(ActivityFilter filter, string search) =>
            (search.Length == 0 || FilePath.Contains(search, StringComparison.OrdinalIgnoreCase))
            && Matches(filter);

        public bool Matches(ActivityFilter filter) =>
            (IsUpload && filter.HasFlag(ActivityFilter.Uploads))
            || (Verdict is "malicious" or "suspicious" && filter.HasFlag(ActivityFilter.Threats))
            || (Verdict == "skipped" && filter.HasFlag(ActivityFilter.Skipped))
            || (Verdict == "error" && filter.HasFlag(ActivityFilter.Errors));

        public CancellationToken CancelToken => cancel.Token;
        public bool CanCancel => !cancelRequested && Stage != UploadStage.Done;

        public UploadStage Stage => stage;
        public string Verdict { get; private set; } = "";
        public string VerdictText { get; private set; } = "";
        public bool IsDone => stage == UploadStage.Done;
        public double Progress => progress;

        public string StatusText
        {
            get
            {
                if (cancelRequested && stage != UploadStage.Done)
                    return Loc.T("Cancelling...");
                switch (stage)
                {
                    case UploadStage.Waiting:
                        return Loc.T("Waiting to upload...");
                    case UploadStage.Uploading:
                        double seconds = timer.Elapsed.TotalSeconds;
                        long sent = (long)(payloadBytes * progress / 100);
                        string speed = seconds > 0.05 && sent > 0 ? CloudUploadItem.FormatBytes((long)(sent / seconds)) : "—";
                        string text = Loc.F("Uploading to Scan Cloud... {0}/sec", speed);
                        if (payloadBytes > 0 && payloadBytes < Size)
                            text += " " + Loc.F("(compression {0:0.0}%)", (1 - (double)payloadBytes / Size) * 100);
                        return text;
                    case UploadStage.Analyzing:
                        return Loc.T("Classifying in Scan Cloud...");
                    default:
                        return VerdictText;
                }
            }
        }

        public void Update(UploadStage newStage, double newProgress, long payload)
        {
            if (payload > 0)
                payloadBytes = payload;
            if (newStage == UploadStage.Analyzing && payload > 0)
                BytesSent = true;
            if (newStage == UploadStage.Uploading && stage != UploadStage.Uploading)
                timer.Restart();
            bool changed = newStage != stage || Math.Abs(newProgress - progress) >= 1 || newStage == UploadStage.Analyzing;
            stage = newStage;
            progress = newStage == UploadStage.Analyzing ? 100 : newProgress;
            if (!changed) return;
            Notify(nameof(Stage));
            Notify(nameof(Progress));
            Notify(nameof(StatusText));
            Notify(nameof(CanCancel));
        }

        internal void Tick()
        {
            if (stage == UploadStage.Uploading)
                Notify(nameof(StatusText));
        }

        public void RequestCancel()
        {
            if (!CanCancel) return;
            cancelRequested = true;
            Notify(nameof(StatusText));
            Notify(nameof(CanCancel));
            try { cancel.Cancel(); } catch (ObjectDisposedException) { }
        }

        public bool WasCancelled => cancelRequested;

        internal void Close(CloudScanResult? result)
        {
            if (stage == UploadStage.Done) return;
            stage = UploadStage.Done;
            Verdict = result?.Verdict ?? "skipped";
            string note = result == null ? ""
                : !string.IsNullOrEmpty(result.ThreatName) ? result.ThreatName
                : result.Verdict is "skipped" or "error" ? result.Detail : "";
            VerdictText = result == null ? Loc.T("Stopped")
                : string.IsNullOrEmpty(note) ? result.VerdictText
                : result.VerdictText + ": " + note;
            timer.Stop();
            cancel.Dispose();
            Notify(nameof(Stage));
            Notify(nameof(IsDone));
            Notify(nameof(Verdict));
            Notify(nameof(StatusText));
            Notify(nameof(CanCancel));
        }

        private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public static string FormatBytes(long bytes)
        {
            double size = bytes;
            string[] units = { "B", "KB", "MB", "GB" };
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return $"{size:0.#} {units[unit]}";
        }
    }

    public sealed class CloudUploadTracker : INotifyPropertyChanged
    {
        public static CloudUploadTracker Instance { get; } = new();

        private const int MaxHistory = 1000;

        private readonly Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        private readonly object sync = new();
        private readonly List<CloudUploadItem> active = new();
        private readonly List<CloudUploadItem> history = new();
        private readonly Stopwatch clock = new();
        private long uploadedFiles, uploadedBytes, knownFiles;
        private int refreshScheduled;
        private DispatcherTimer? elapsedTimer;
        private string phase = "";
        private string currentFile = "";
        private int done, total, threats, queued, skipped, errors, started;
        private int found, folders, fingerprinted, answered, awaiting, connections;
        private bool collecting, collectionDone;
        private ActivityFilter filter = ActivityFilter.All;
        private string search = "";
        private readonly Stopwatch rateClock = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? ThreatsRequested;

        public ObservableCollection<CloudUploadItem> Shown { get; } = new();

        public ActivityFilter Filter
        {
            get => filter;
            set
            {
                if (filter == value) return;
                filter = value;
                Notify(nameof(Filter));
                Notify(nameof(ShowUploads));
                Notify(nameof(ShowThreats));
                Notify(nameof(ShowSkipped));
                Notify(nameof(ShowErrors));
                ScheduleRefresh();
            }
        }

        public string SearchText
        {
            get => search;
            set
            {
                value = (value ?? "").Trim();
                if (search == value) return;
                search = value;
                Notify(nameof(SearchText));
                ScheduleRefresh();
            }
        }

        public bool IsFiltered => filter != ActivityFilter.All || search.Length > 0;

        public void ResetFilter()
        {
            SearchText = "";
            Filter = ActivityFilter.All;
            Notify(nameof(IsFiltered));
        }

        public bool ShowUploads { get => filter.HasFlag(ActivityFilter.Uploads); set => SetFlag(ActivityFilter.Uploads, value); }
        public bool ShowThreats { get => filter.HasFlag(ActivityFilter.Threats); set => SetFlag(ActivityFilter.Threats, value); }
        public bool ShowSkipped { get => filter.HasFlag(ActivityFilter.Skipped); set => SetFlag(ActivityFilter.Skipped, value); }
        public bool ShowErrors { get => filter.HasFlag(ActivityFilter.Errors); set => SetFlag(ActivityFilter.Errors, value); }

        private void SetFlag(ActivityFilter flag, bool on) => Filter = on ? filter | flag : filter & ~flag;

        public string UploadsChip { get; private set; } = "";
        public string ThreatsChip { get; private set; } = "";
        public string SkippedChip { get; private set; } = "";
        public string ErrorsChip { get; private set; } = "";
        public string EmptyText { get; private set; } = "";
        public bool IsScanning { get; private set; }
        public string Title { get; private set; } = "";
        public double Percent { get; private set; }
        public bool Indeterminate { get; private set; }
        public string CurrentFile { get; private set; } = "";
        public string PipelineText { get; private set; } = "";
        public string Elapsed { get; private set; } = "";
        public string Remaining { get; private set; } = "—";
        public string ScannedText { get; private set; } = "0";
        public string ThreatsText { get; private set; } = "0";
        public bool HasThreats { get; private set; }
        public string UploadedText { get; private set; } = "0";
        public bool HasUploads { get; private set; }
        public string MoreText { get; private set; } = "";
        public long UploadedFiles => Interlocked.Read(ref uploadedFiles);
        public long UploadedBytes => Interlocked.Read(ref uploadedBytes);
        public long KnownFiles => Interlocked.Read(ref knownFiles);

        public void Begin()
        {
            Interlocked.Exchange(ref uploadedFiles, 0);
            Interlocked.Exchange(ref uploadedBytes, 0);
            Interlocked.Exchange(ref knownFiles, 0);
            lock (sync)
            {
                active.Clear();
                history.Clear();
            }
            phase = Loc.T("Preparing scan...");
            currentFile = "";
            done = 0;
            total = 0;
            threats = 0;
            queued = 0;
            found = folders = fingerprinted = answered = awaiting = connections = 0;
            collecting = true;
            collectionDone = false;
            skipped = 0;
            errors = 0;
            started = 0;
            rateClock.Reset();
            OnUi(() =>
            {
                IsScanning = true;
                clock.Restart();
                elapsedTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (s, e) => Refresh(), dispatcher);
                elapsedTimer.Start();
            });
            ScheduleRefresh();
        }

        public void SetPhase(string text)
        {
            phase = text;
            ScheduleRefresh();
        }

        public void SetProgress(int doneFiles, int totalFiles, int threatCount = 0, int skippedCount = 0, int errorCount = 0)
        {
            skipped = skippedCount;
            errors = errorCount;
            if (totalFiles > 0 && !rateClock.IsRunning)
                rateClock.Restart();
            done = doneFiles;
            total = totalFiles;
            threats = threatCount;
            ScheduleRefresh();
        }

        public void AddEvent(CloudScanResult result)
        {
            var item = new CloudUploadItem { FilePath = result.FilePath, Size = result.Size };
            item.Close(result);
            lock (sync)
            {
                history.Insert(0, item);
                if (history.Count > MaxHistory)
                    history.RemoveAt(history.Count - 1);
            }
            ScheduleRefresh();
        }

        public void SetFound(int count, int folderCount, bool stillCollecting)
        {
            if (!stillCollecting)
                collectionDone = true;
            else if (collectionDone)
                return;
            found = count;
            if (folderCount >= 0)
                folders = folderCount;
            collecting = stillCollecting;
            ScheduleRefresh();
        }

        public void AddFingerprinted() => Interlocked.Increment(ref fingerprinted);
        public void CheckStarted() => Interlocked.Increment(ref awaiting);

        public void CheckFinished()
        {
            Interlocked.Decrement(ref awaiting);
            Interlocked.Increment(ref answered);
        }

        public void SetConnections(int count)
        {
            connections = count;
            ScheduleRefresh();
        }

        public string PipelineSummary() => Loc.F("Found {0:N0}, fingerprinted {1:N0}, answered by the cloud {2:N0}, waiting for the cloud {3:N0}, connections {4}",
            Volatile.Read(ref found), Volatile.Read(ref fingerprinted), Volatile.Read(ref answered), Math.Max(0, Volatile.Read(ref awaiting)), Volatile.Read(ref connections));

        public void SetQueued(int count)
        {
            queued = Math.Max(0, count);
            ScheduleRefresh();
        }

        public void RequestThreats() => ThreatsRequested?.Invoke();

        public void SetCurrentFile(string path)
        {
            currentFile = path;
            ScheduleRefresh();
        }

        public void End()
        {
            lock (sync)
            {
                foreach (var item in active)
                {
                    item.Close(null);
                    history.Insert(0, item);
                }
                active.Clear();
            }
            OnUi(() =>
            {
                IsScanning = false;
                clock.Stop();
                rateClock.Stop();
                elapsedTimer?.Stop();
            });
            ScheduleRefresh();
        }

        public void CountKnown() => Interlocked.Increment(ref knownFiles);

        public CloudUploadItem Start(string path, long size)
        {
            var item = new CloudUploadItem { FilePath = path, Size = size, IsUpload = true };
            lock (sync) active.Add(item);
            ScheduleRefresh();
            return item;
        }

        public void Update(CloudUploadItem item, UploadStage stage, double progress, long payload)
        {
            item.Update(stage, progress, payload);
            if (stage == UploadStage.Analyzing && payload > 0)
            {
                Interlocked.Increment(ref started);
                Interlocked.Increment(ref uploadedFiles);
                Interlocked.Add(ref uploadedBytes, payload);
            }
        }

        public void Finish(CloudUploadItem item, CloudScanResult? result)
        {
            bool sent = item.BytesSent;
            item.IsUpload = sent;
            item.Close(result);
            lock (sync)
            {
                active.Remove(item);
                if (sent || result?.Verdict is "malicious" or "suspicious")
                {
                    history.Insert(0, item);
                    if (history.Count > MaxHistory)
                        history.RemoveAt(history.Count - 1);
                }
            }
            ScheduleRefresh();
        }

        private void ScheduleRefresh()
        {
            if (Interlocked.CompareExchange(ref refreshScheduled, 1, 0) == 0)
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    Interlocked.Exchange(ref refreshScheduled, 0);
                    Refresh();
                }));
        }

        private void Refresh()
        {
            List<CloudUploadItem> wanted;
            int waiting;
            lock (sync)
            {
                var busy = active.Where(i => i.Stage != UploadStage.Waiting).ToList();
                waiting = active.Count - busy.Count + queued;
                var f = filter;
                string q = search;
                wanted = busy.Where(i => i.Stage == UploadStage.Uploading && i.Matches(f, q))
                    .Concat(busy.Where(i => i.Stage != UploadStage.Uploading && i.Matches(f, q)))
                    .Concat(history.Where(i => i.Matches(f, q))).ToList();
            }

            var keep = new HashSet<CloudUploadItem>(wanted);
            for (int i = Shown.Count - 1; i >= 0; i--)
                if (!keep.Contains(Shown[i]))
                    Shown.RemoveAt(i);
            for (int i = 0; i < wanted.Count; i++)
            {
                if (i < Shown.Count && ReferenceEquals(Shown[i], wanted[i]))
                    continue;
                int at = Shown.IndexOf(wanted[i]);
                if (at >= 0)
                    Shown.Move(at, i);
                else
                    Shown.Insert(i, wanted[i]);
            }

            int d = done, t = total, th = threats;
            bool gathering = IsScanning && collecting;
            Indeterminate = gathering || t <= 0;
            Percent = !gathering && t > 0 ? Math.Min(100, d * 100.0 / t) : 0;
            Title = gathering
                ? Loc.F("Collecting files: {0:N0} found in {1:N0} folders", Volatile.Read(ref found), Volatile.Read(ref folders))
                : t > 0 ? string.Format("{0} ({1}%)...", phase.TrimEnd('.', ' ', '…'), (int)Percent) : phase;
            CurrentFile = currentFile;
            string foundText = collecting
                ? Loc.F("Collecting files: {0:N0} found so far", Volatile.Read(ref found))
                : Loc.F("{0:N0} files found", Volatile.Read(ref found));
            PipelineText = foundText + " · " + Loc.F("{0:N0} fingerprinted", Volatile.Read(ref fingerprinted))
                + " · " + Loc.F("{0:N0} answered by the cloud", Volatile.Read(ref answered))
                + " · " + Loc.F("{0:N0} waiting for the cloud", Math.Max(0, Volatile.Read(ref awaiting)))
                + " · " + (Volatile.Read(ref connections) > 0 ? Loc.F("{0} connection(s)", Volatile.Read(ref connections)) : Loc.T("Connecting..."));
            TimeSpan e = clock.Elapsed;
            Elapsed = e.TotalHours >= 1 ? e.ToString(@"h\:mm\:ss") : e.TotalMinutes >= 1 ? e.ToString(@"m\:ss") : $"{(int)e.TotalSeconds}s";
            Remaining = RemainingTime(d, t);
            ScannedText = t > 0 ? $"{d:N0} / {t:N0}" : $"{d:N0}";
            ThreatsText = th.ToString("N0");
            HasThreats = th > 0;
            long up = UploadedFiles, upBytes = UploadedBytes;
            UploadedText = up > 0 ? $"{up:N0} ({CloudUploadItem.FormatBytes(upBytes)})" : "0";
            foreach (var item in Shown)
                if (!item.IsDone)
                    item.Tick();

            MoreText = waiting > 0 ? Loc.F("{0:N0} file(s) waiting to upload", waiting) : "";
            HasUploads = IsScanning;
            UploadsChip = Loc.F("Uploads ({0:N0})", Volatile.Read(ref started));
            ThreatsChip = Loc.F("Threats ({0:N0})", th);
            SkippedChip = Loc.F("Skipped ({0:N0})", skipped);
            ErrorsChip = Loc.F("Errors ({0:N0})", errors);
            EmptyText = Shown.Count > 0 ? ""
                : filter == ActivityFilter.None ? Loc.T("Choose what to show with the buttons above.")
                : Loc.T("Nothing to show for the selected filters yet.");
            Notify(nameof(IsScanning));
            Notify(nameof(Indeterminate));
            Notify(nameof(Percent));
            Notify(nameof(Title));
            Notify(nameof(CurrentFile));
            Notify(nameof(PipelineText));
            Notify(nameof(Elapsed));
            Notify(nameof(Remaining));
            Notify(nameof(ScannedText));
            Notify(nameof(ThreatsText));
            Notify(nameof(HasThreats));
            Notify(nameof(UploadedText));
            Notify(nameof(HasUploads));
            Notify(nameof(MoreText));
            Notify(nameof(UploadsChip));
            Notify(nameof(ThreatsChip));
            Notify(nameof(SkippedChip));
            Notify(nameof(ErrorsChip));
            Notify(nameof(EmptyText));
            Notify(nameof(IsFiltered));
        }

        private string RemainingTime(int d, int t)
        {
            if (!IsScanning || t <= 0)
                return "—";
            if (d >= t)
                return "0s";
            double seconds = rateClock.Elapsed.TotalSeconds;
            if (d < 3 || seconds < 2)
                return "—";
            var eta = TimeSpan.FromSeconds(seconds / d * (t - d));
            if (eta.TotalHours >= 1)
                return Loc.F("~{0:0.#}h", eta.TotalHours);
            if (eta.TotalMinutes >= 1)
                return Loc.F("~{0}m", (int)eta.TotalMinutes);
            return Loc.T("<1m");
        }

        private void OnUi(Action action)
        {
            if (dispatcher.CheckAccess())
                action();
            else
                dispatcher.BeginInvoke(action);
        }

        private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
