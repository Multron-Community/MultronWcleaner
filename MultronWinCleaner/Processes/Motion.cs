using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Animation;

namespace MultronWinCleaner.Processes
{
    public static class Motion
    {
        public const string SettingKey = "animations";

        private static bool fastTimelines;

        public static bool Enabled { get; private set; } = true;

        public static event Action? Changed;

        public static void Load()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.txt");
                Enabled = !File.Exists(path) || !File.ReadAllLines(path).Any(l => l.Trim().Equals(SettingKey + ":0", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                Enabled = true;
            }
            if (!Enabled)
                FinishTransitionsAtOnce();
        }

        public static void Set(bool enabled)
        {
            if (Enabled == enabled)
                return;
            Enabled = enabled;
            if (!enabled)
                FinishTransitionsAtOnce();
            Changed?.Invoke();
        }

        public static void Reveal(FrameworkElement element, double offsetY = 14, int delay = 0)
        {
            if (!Enabled || element == null)
                return;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var shift = new System.Windows.Media.TranslateTransform(0, offsetY);
            element.RenderTransform = shift;
            var move = new DoubleAnimation(offsetY, 0, TimeSpan.FromMilliseconds(400)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(delay), SpeedRatio = 1 };
            move.Completed += (s, e) =>
            {
                if (ReferenceEquals(element.RenderTransform, shift))
                    element.ClearValue(UIElement.RenderTransformProperty);
            };
            shift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, move);
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(360)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(delay), SpeedRatio = 1, FillBehavior = FillBehavior.Stop };
            if (delay > 0)
                element.Opacity = 0;
            fade.Completed += (s, e) => element.Opacity = 1;
            element.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        public static void Glide(System.Windows.Controls.Primitives.RangeBase bar, double value, int milliseconds = 450)
        {
            if (!Enabled || !bar.IsVisible)
            {
                bar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
                bar.Value = value;
                return;
            }
            bar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, new DoubleAnimation(value, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                SpeedRatio = 1
            });
        }

        public static void Lift(FrameworkElement card, bool up)
        {
            if (!Enabled)
                return;
            if (card.RenderTransform is not System.Windows.Media.TranslateTransform shift || shift.IsFrozen)
            {
                shift = new System.Windows.Media.TranslateTransform();
                card.RenderTransform = shift;
            }
            shift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(up ? -3 : 0, TimeSpan.FromMilliseconds(up ? 160 : 220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                SpeedRatio = 1
            });
        }

        private static readonly System.Collections.Generic.HashSet<string> SkipWindows = new() { "Notify", "MemoryMon", "MainWindow", "NewStartupItemDetected" };

        public static void RegisterWindows()
        {
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Window window || !ReferenceEquals(e.OriginalSource, window) || SkipWindows.Contains(window.GetType().Name))
                return;
            if (window.Content is FrameworkElement first)
                Reveal(first, 12);
            window.IsVisibleChanged += (s, a) =>
            {
                if (window.IsVisible && window.Content is FrameworkElement root)
                    Reveal(root, 12);
            };
        }

        private static void FinishTransitionsAtOnce()
        {
            if (fastTimelines)
                return;
            fastTimelines = true;
            foreach (var type in new[]
            {
                typeof(DoubleAnimation), typeof(ColorAnimation), typeof(ThicknessAnimation), typeof(PointAnimation),
                typeof(DoubleAnimationUsingKeyFrames), typeof(ColorAnimationUsingKeyFrames), typeof(ObjectAnimationUsingKeyFrames), typeof(Storyboard)
            })
            {
                try
                {
                    Timeline.SpeedRatioProperty.OverrideMetadata(type, new PropertyMetadata(40.0));
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
