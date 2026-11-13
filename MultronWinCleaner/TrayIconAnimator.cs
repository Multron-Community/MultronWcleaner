using Hardcodet.Wpf.TaskbarNotification;
using MultronWinCleaner;
using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace Multron_Win_Cleaner
{
    public class TrayIconAnimator
    {
        private const int FrameCount = 12;
        private readonly TaskbarIcon _trayIcon;
        private readonly string _idleIconUri;
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<string, System.Drawing.Icon> _icons = new();
        private System.Drawing.Bitmap? _base;
        private System.Drawing.Icon[]? _frames;
        private int _frameIndex;
        private bool _warning;
        private TrayBadge? _badge;

        public TrayIconAnimator(TaskbarIcon trayIcon, string idleIconUri, int fps = 12)
        {
            _trayIcon = trayIcon ?? throw new ArgumentNullException(nameof(trayIcon));
            _idleIconUri = idleIconUri;
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000.0 / fps)
            };
            _timer.Tick += OnTick;
        }

        public bool IsSpinning => _timer.IsEnabled;

        internal TrayBadge? Badge => _badge;

        public void Start()
        {
            if (_timer.IsEnabled) return;
            _frameIndex = 0;
            _timer.Start();
            OnTick(this, EventArgs.Empty);
        }

        public void Stop()
        {
            _timer.Stop();
            ShowIdleIcon();
        }

        public void SetWarning(bool warning)
        {
            _warning = warning;
            if (!_timer.IsEnabled)
                ShowIdleIcon();
        }

        internal void SetBadge(TrayBadge? badge)
        {
            _badge = badge;
            if (!_timer.IsEnabled)
                ShowIdleIcon();
        }

        private System.Drawing.Bitmap? Base()
        {
            try
            {
                return _base ??= ScanTrayIcons.LoadBase(_idleIconUri);
            }
            catch
            {
                return null;
            }
        }

        private void ShowIdleIcon()
        {
            var image = Base();
            if (image == null)
                return;
            TrayBadge? badge = _badge == TrayBadge.Danger ? TrayBadge.Danger : _warning ? TrayBadge.Warning : _badge;
            string key = badge?.ToString() ?? "none";
            if (!_icons.TryGetValue(key, out var icon))
                _icons[key] = icon = ScanTrayIcons.CreateWithBadge(image, badge);
            _trayIcon.Icon = icon;
        }

        private void OnTick(object? sender, EventArgs e)
        {
            var image = Base();
            if (image == null)
                return;
            _frames ??= ScanTrayIcons.CreateSpinnerFrames(image, FrameCount);
            _trayIcon.Icon = _frames[_frameIndex];
            _frameIndex = (_frameIndex + 1) % _frames.Length;
        }
    }
}
