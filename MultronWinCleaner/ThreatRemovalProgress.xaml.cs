using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MultronWinCleaner
{
    public partial class ThreatRemovalProgress : Window
    {
        public enum FileOutcome { Removed, Reboot, Failed, Skipped }

        public sealed class Line
        {
            public string Text { get; init; } = "";
            public Brush Brush { get; init; } = Brushes.Gray;
            public FontWeight Weight { get; init; } = FontWeights.Normal;
            public Thickness Indent { get; init; }
        }

        private static readonly Brush GreenBrush = Frozen("#FF28A745");
        private static readonly Brush OrangeBrush = Frozen("#FFFF9800");
        private static readonly Brush RedBrush = Frozen("#FFDC3545");
        private static readonly Brush BlueBrush = Frozen("#FF1E88E5");

        private readonly ObservableCollection<Line> lines = new();
        private readonly CancellationTokenSource cts = new();
        private readonly DispatcherTimer elapsedTimer;
        private readonly Stopwatch stopwatch = new();
        private readonly int total;
        private readonly bool quarantine;
        private int processed, removed, reboot, failed;
        private string lastStep = "";
        private bool finished;

        public string? LogPath { get; set; }
        public CancellationToken Token => cts.Token;

        public ThreatRemovalProgress(int total, bool quarantine)
        {
            InitializeComponent();
            this.total = total;
            this.quarantine = quarantine;
            DetailsList.ItemsSource = lines;

            string title = quarantine ? Loc.T("Quarantining Files") : Loc.T("Removing Threats");
            Title = title;
            WindowTitleText.Text = title;
            TitleText.Text = quarantine ? Loc.T("Moving files to quarantine...") : Loc.T("Removing threats...");
            RemovedLabelText.Text = quarantine ? Loc.T("Quarantined") : Loc.T("Removed");
            if (quarantine)
            {
                StateCircle.Fill = OrangeBrush;
                StateGlyph.Text = "";
                RemovalProgressBar.Foreground = OrangeBrush;
            }
            SubtitleText.Text = Loc.F("{0} of {1} file(s) processed", 0, total);

            elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            elapsedTimer.Tick += (s, e) => UpdateElapsed();
            SpinnerRotation.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever, SpeedRatio = 1 });
            stopwatch.Start();
            elapsedTimer.Start();
        }

        private static Brush Frozen(string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }

        private Brush SecondaryBrush => (Brush)FindResource("TextSecondary");
        private Brush PrimaryBrush => (Brush)FindResource("TextPrimary");

        private void AddLine(string text, Brush brush, FontWeight weight, double indent)
        {
            if (brush is SolidColorBrush solid)
                brush = MultronWinCleaner.Processes.ThemeColors.ReadableBrush(solid.Color);
            lines.Add(new Line { Text = text, Brush = brush, Weight = weight, Indent = new Thickness(indent, 0, 0, 0) });
            DetailsList.ScrollIntoView(lines[^1]);
        }

        public void BeginFile(int index, string path, string threat)
        {
            if (finished) return;
            lastStep = "";
            CurrentFileText.Text = Loc.F("{0} / {1}: {2}", index + 1, total, Path.GetFileName(path));
            CurrentFileText.ToolTip = path;
            CurrentStepText.Text = "";
            if (lines.Count > 0)
                AddLine("", SecondaryBrush, FontWeights.Normal, 0);
            AddLine(Loc.F("[{0}/{1}] {2}", index + 1, total, path), PrimaryBrush, FontWeights.SemiBold, 0);
            if (!string.IsNullOrEmpty(threat))
                AddLine(Loc.F("Threat: {0}", threat), RedBrush, FontWeights.Normal, 14);
        }

        public void Step(string text)
        {
            if (finished || string.IsNullOrWhiteSpace(text) || text == lastStep) return;
            lastStep = text;
            CurrentStepText.Text = text;
            AddLine("• " + text, SecondaryBrush, FontWeights.Normal, 14);
        }

        public void EndFile(FileOutcome outcome, string message)
        {
            processed++;
            Brush brush;
            switch (outcome)
            {
                case FileOutcome.Removed:
                    removed++;
                    brush = GreenBrush;
                    break;
                case FileOutcome.Reboot:
                    reboot++;
                    brush = OrangeBrush;
                    break;
                case FileOutcome.Skipped:
                    brush = OrangeBrush;
                    break;
                default:
                    failed++;
                    brush = RedBrush;
                    break;
            }
            string glyph = outcome switch
            {
                FileOutcome.Removed => "✓ ",
                FileOutcome.Reboot => "⟳ ",
                FileOutcome.Skipped => "– ",
                _ => "✕ "
            };
            AddLine(glyph + message, brush, FontWeights.SemiBold, 14);

            RemovedCountText.Text = removed.ToString();
            RebootCountText.Text = reboot.ToString();
            FailedCountText.Text = failed.ToString();
            FailedCountText.Foreground = failed > 0 ? RedBrush : PrimaryBrush;

            double percent = total == 0 ? 100 : (double)processed / total * 100;
            RemovalProgressBar.Value = percent;
            PercentText.Text = $"{(int)percent}%";
            SubtitleText.Text = Loc.F("{0} of {1} file(s) processed", processed, total);
        }

        public void Finish(bool stopped)
        {
            finished = true;
            stopwatch.Stop();
            elapsedTimer.Stop();
            UpdateElapsed();
            Spinner.Visibility = Visibility.Collapsed;
            SpinnerRotation.BeginAnimation(RotateTransform.AngleProperty, null);

            Brush brush = stopped || failed > 0 || reboot > 0 ? OrangeBrush : GreenBrush;
            if (failed > 0 && removed == 0 && reboot == 0)
                brush = RedBrush;
            StateCircle.Fill = brush;
            StateGlyph.Text = brush == GreenBrush ? "" : brush == RedBrush ? "" : "";
            RemovalProgressBar.Foreground = brush;
            if (!stopped)
            {
                RemovalProgressBar.Value = 100;
                PercentText.Text = "100%";
            }

            string done = quarantine ? Loc.F("{0} quarantined", removed) : Loc.F("{0} removed", removed);
            var parts = new[]
            {
                done,
                reboot > 0 ? Loc.F("{0} will be deleted at the next restart", reboot) : "",
                failed > 0 ? Loc.F("{0} failed", failed) : "",
                stopped ? Loc.F("{0} not processed", total - processed) : ""
            }.Where(p => p.Length > 0);
            string summary = string.Join(", ", parts);

            TitleText.Text = stopped ? Loc.T("Removal stopped")
                : failed > 0 ? Loc.T("Removal finished with errors")
                : quarantine ? Loc.T("Quarantine complete") : Loc.T("Removal complete");
            SubtitleText.Text = summary;
            CurrentFileText.Text = "";
            CurrentStepText.Text = reboot > 0 ? Loc.T("Restart your computer to finish removing the locked files.") : "";

            AddLine("", SecondaryBrush, FontWeights.Normal, 0);
            AddLine(summary, brush == RedBrush ? RedBrush : brush == GreenBrush ? GreenBrush : BlueBrush, FontWeights.Bold, 0);

            StopButton.Visibility = Visibility.Collapsed;
            DoneButton.Visibility = Visibility.Visible;
            CloseButton.IsEnabled = true;

            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            if (IsVisible)
                Activate();
        }

        private void UpdateElapsed()
        {
            TimeSpan t = stopwatch.Elapsed;
            ElapsedText.Text = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            StopButton.IsEnabled = false;
            CurrentStepText.Text = Loc.T("Stopping after the current file...");
            cts.Cancel();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            if (finished)
                Close();
        }

        private void OpenLog_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(LogPath) || !File.Exists(LogPath)) return;
            try
            {
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + LogPath + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CopyDetails_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, lines.Select(l => (l.Indent.Left > 0 ? "    " : "") + l.Text)));
            }
            catch (Exception) { }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!finished)
                e.Cancel = true;
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            elapsedTimer.Stop();
            cts.Dispose();
            base.OnClosed(e);
        }
    }
}
