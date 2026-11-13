using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MultronWinCleaner
{
    public partial class Notify : Window
    {
        
        private readonly Action? onClick;

        public Notify(string status, string proc, string? cleaned, Action? onClick) : this(status, proc, cleaned)
        {
            this.onClick = onClick;
            Cursor = Cursors.Hand;
            if (onClick != null)
                DetailsButton.Visibility = Visibility.Visible;
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            OpenDetails();
        }

        private void DetailsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenDetails();
        }

        private void OpenDetails()
        {
            if (onClick == null)
                return;
            Close();
            onClick();
        }

        public Notify(string status, string proc, string? cleaned)
        {
            InitializeComponent();
            
            Status.Text = status;
            DetailsText.Text = proc.Trim();
            AmountText.Text = cleaned;
            if (string.IsNullOrWhiteSpace(cleaned))
                AmountBadge.Visibility = Visibility.Collapsed;
            TimeText.Text = DateTime.Now.ToString("t", Loc.Culture);
            Styled(Kind.Info, "\uE73E");
            try
            {
                (Application.Current?.MainWindow as Multron_Win_Cleaner.MainWindow)?.LogNotification(status, proc.Replace("\r", " ").Replace("\n", " "), cleaned);
            }
            catch { }
        }
        public enum Kind { Info, Success, Warning, Danger }

        public Notify Styled(Kind kind, string? glyph = null)
        {
            var (hex, icon) = kind switch
            {
                Kind.Success => ("#FF2EB872", "\uE73E"),
                Kind.Warning => ("#FFFF9800", "\uE7BA"),
                Kind.Danger => ("#FFEF5350", "\uE7BA"),
                _ => ("#FF42A5F5", "\uE946")
            };
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var light = Color.FromRgb((byte)Math.Min(255, color.R + 45), (byte)Math.Min(255, color.G + 40), (byte)Math.Min(255, color.B + 30));
            var deep = Color.FromRgb((byte)(color.R * 0.78), (byte)(color.G * 0.78), (byte)(color.B * 0.78));
            IconText.Text = glyph ?? icon;
            IconText.Foreground = Brushes.White;
            IconBorder.Background = new LinearGradientBrush(light, deep, new Point(0, 0), new Point(1, 1));
            IconBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = color, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.45 };
            IconRing.Stroke = new SolidColorBrush(color);
            var wash = new RadialGradientBrush { Center = new Point(0.08, 0.0), GradientOrigin = new Point(0.08, 0.0), RadiusX = 0.95, RadiusY = 1.25 };
            wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x30, color.R, color.G, color.B), 0));
            wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, color.R, color.G, color.B), 1));
            Wash.Background = wash;
            Card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, color.R, color.G, color.B));
            AmountBadge.Background = new SolidColorBrush(Color.FromArgb(0x24, color.R, color.G, color.B));
            AmountText.Foreground = MultronWinCleaner.Processes.ThemeColors.ReadableBrush(color);
            var fillColor = kind == Kind.Warning ? MultronWinCleaner.Processes.ThemeColors.WarningFill : color;
            var fill = new LinearGradientBrush(Color.FromRgb((byte)Math.Min(255, fillColor.R + 25), (byte)Math.Min(255, fillColor.G + 25), (byte)Math.Min(255, fillColor.B + 25)), fillColor, 90);
            DetailsButton.Background = fill;
            DetailsButton.BorderBrush = new SolidColorBrush(fillColor);
            return this;
        }

        public Notify AsWarning() => Styled(Kind.Warning);

        public Notify WithAction(string text)
        {
            DetailsButton.Content = text;
            return this;
        }

        private static readonly List<Notify> openNotifications = new List<Notify>();

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (openNotifications.Count == 0)
            {
                position = ReadPosition();
                layout = ReadLayout();
            }
            openNotifications.Add(this);
            Closed += (s, args) =>
            {
                openNotifications.Remove(this);
                ArrangeNotifications();
            };
            SizeChanged += (s, args) => ArrangeNotifications();
            ArrangeNotifications();
            Dispatcher.BeginInvoke(ArrangeNotifications, System.Windows.Threading.DispatcherPriority.Loaded);
            if (Processes.Motion.Enabled && Content is FrameworkElement root)
            {
                bool fromLeft = Left < SystemParameters.WorkArea.Left + SystemParameters.WorkArea.Width / 2;
                var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
                var slide = new System.Windows.Media.TranslateTransform(fromLeft ? -40 : 40, 0);
                root.RenderTransform = slide;
                var move = new System.Windows.Media.Animation.DoubleAnimation(slide.X, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease, SpeedRatio = 1 };
                move.Completed += (s2, e2) =>
                {
                    if (ReferenceEquals(root.RenderTransform, slide))
                        root.ClearValue(RenderTransformProperty);
                };
                slide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, move);
                root.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { SpeedRatio = 1, FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop });
                if (IconBorder.RenderTransform is ScaleTransform pop && !pop.IsFrozen)
                {
                    var bounce = new System.Windows.Media.Animation.DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(520))
                    {
                        EasingFunction = new System.Windows.Media.Animation.BackEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Amplitude = 0.55 },
                        BeginTime = TimeSpan.FromMilliseconds(120),
                        SpeedRatio = 1
                    };
                    pop.ScaleX = 0.4;
                    pop.ScaleY = 0.4;
                    pop.BeginAnimation(ScaleTransform.ScaleXProperty, bounce);
                    pop.BeginAnimation(ScaleTransform.ScaleYProperty, bounce);
                }
                if (IconRing.RenderTransform is ScaleTransform ring && !ring.IsFrozen)
                {
                    var spread = new System.Windows.Media.Animation.DoubleAnimation(1, 1.7, TimeSpan.FromMilliseconds(900)) { BeginTime = TimeSpan.FromMilliseconds(380), SpeedRatio = 1, EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
                    ring.BeginAnimation(ScaleTransform.ScaleXProperty, spread);
                    ring.BeginAnimation(ScaleTransform.ScaleYProperty, spread);
                    IconRing.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.7, 0, TimeSpan.FromMilliseconds(900)) { BeginTime = TimeSpan.FromMilliseconds(380), SpeedRatio = 1 });
                }
            }
        }

        public const string PositionSettingKey = "notifyposition";

        public enum Corner { BottomRight, BottomLeft, TopRight, TopLeft }

        public const string LayoutSettingKey = "notifylayout";

        public enum Layout { Stacked, Overlapping }

        public static Corner ReadPosition() => (Corner)ReadSetting(PositionSettingKey, typeof(Corner));

        public static Layout ReadLayout() => (Layout)ReadSetting(LayoutSettingKey, typeof(Layout));

        private static int ReadSetting(string key, Type enumType)
        {
            try
            {
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.txt");
                if (System.IO.File.Exists(path))
                {
                    string? line = System.IO.File.ReadAllLines(path)
                        .FirstOrDefault(l => l.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase));
                    if (line != null && int.TryParse(line.Substring(key.Length + 1).Trim(), out int value) && Enum.IsDefined(enumType, value))
                        return value;
                }
            }
            catch
            {
            }
            return 0;
        }

        private static Corner position = Corner.BottomRight;
        private static Layout layout = Layout.Stacked;

        private static void ArrangeNotifications()
        {
            var workArea = SystemParameters.WorkArea;
            bool top = position == Corner.TopRight || position == Corner.TopLeft;
            bool left = position == Corner.BottomLeft || position == Corner.TopLeft;
            double next = top ? workArea.Top + 10 : workArea.Bottom - 10;
            foreach (Notify notify in openNotifications)
            {
                double height = Math.Max(notify.ActualHeight, notify.MinHeight);
                notify.Left = left ? workArea.Left + 10 : workArea.Right - notify.Width - 10;
                notify.Top = top ? next : next - height;
                if (layout == Layout.Overlapping)
                    continue;
                next = top ? next + height + 10 : notify.Top - 10;
            }
        }
        private bool fadingOut;

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!fadingOut && Processes.Motion.Enabled && IsVisible && Content is FrameworkElement root)
            {
                e.Cancel = true;
                fadingOut = true;
                var fade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { SpeedRatio = 1 };
                fade.Completed += (s, a) => Close();
                root.BeginAnimation(OpacityProperty, fade);
                return;
            }
            base.OnClosing(e);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        } 
    }
}
