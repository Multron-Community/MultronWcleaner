using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace MultronWinCleaner.Processes
{
    public static class OrbHover
    {
        public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(OrbHover), new PropertyMetadata(false, OnEnabledChanged));

        private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
            "State", typeof(int), typeof(OrbHover), new PropertyMetadata(-1));

        private static readonly DependencyPropertyDescriptor PressedDescriptor =
            DependencyPropertyDescriptor.FromProperty(ButtonBase.IsPressedProperty, typeof(ButtonBase));

        private static readonly DependencyPropertyDescriptor ContentDescriptor =
            DependencyPropertyDescriptor.FromProperty(ContentControl.ContentProperty, typeof(ContentControl));

        private const int Disabled = 0, Normal = 1, Hover = 2, Pressed = 3;

        public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

        public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

        private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ButtonBase button)
                return;
            if ((bool)e.NewValue)
            {
                button.Loaded += OnLoaded;
                button.MouseEnter += OnMouse;
                button.MouseLeave += OnMouse;
                button.IsEnabledChanged += OnEnabledStateChanged;
                PressedDescriptor.AddValueChanged(button, OnStateChanged);
                ContentDescriptor.AddValueChanged(button, OnContentChanged);
            }
            else
            {
                button.Loaded -= OnLoaded;
                button.MouseEnter -= OnMouse;
                button.MouseLeave -= OnMouse;
                button.IsEnabledChanged -= OnEnabledStateChanged;
                PressedDescriptor.RemoveValueChanged(button, OnStateChanged);
                ContentDescriptor.RemoveValueChanged(button, OnContentChanged);
            }
        }

        private static void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is ButtonBase button)
            {
                ApplyColors(button, false);
                if (!Orbs.Any(r => r.TryGetTarget(out var known) && ReferenceEquals(known, button)))
                    Orbs.Add(new WeakReference<ButtonBase>(button));
                TintAura(button, false);
                Update(button);
                Idle(button);
            }
        }

        private static readonly List<WeakReference<ButtonBase>> Orbs = new();

        public static void RetintAll()
        {
            Orbs.RemoveAll(r => !r.TryGetTarget(out _));
            foreach (var orb in Orbs)
                if (orb.TryGetTarget(out var button))
                {
                    TintAura(button, true);
                    Idle(button);
                }
        }

        public static void SetLevel(ButtonBase button, double fraction, bool show)
        {
            if (Part<FrameworkElement>(button, "OrbLiquid") is not FrameworkElement liquid
                || Part<FrameworkElement>(button, "OrbLiquidLevel") is not FrameworkElement level)
                return;
            var rise = Transform<TranslateTransform>(level);
            if (!show)
            {
                if ((bool)liquid.GetValue(LiquidOnProperty))
                {
                    liquid.SetValue(LiquidOnProperty, false);
                    liquid.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(350)));
                }
                return;
            }
            double height = liquid.ActualHeight > 0 ? liquid.ActualHeight : 130;
            double target = height * (1 - Math.Clamp(fraction, 0, 1));
            if (!(bool)liquid.GetValue(LiquidOnProperty))
            {
                liquid.SetValue(LiquidOnProperty, true);
                rise.BeginAnimation(TranslateTransform.YProperty, null);
                rise.Y = height;
                liquid.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(300)));
                StartWaves(button);
            }
            rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(450)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        private static readonly DependencyProperty LiquidOnProperty =
            DependencyProperty.RegisterAttached("LiquidOn", typeof(bool), typeof(OrbHover), new PropertyMetadata(false));

        private static void StartWaves(ButtonBase button)
        {
            if (!Motion.Enabled)
                return;
            foreach (var (name, from, to, ms, lift) in new[] { ("OrbLiquidFront", 0.0, -130.0, 2200, 0.0), ("OrbLiquidBack", -130.0, 0.0, 3400, -3.0) })
            {
                if (Part<FrameworkElement>(button, name) is not FrameworkElement path)
                    continue;
                var move = Transform<TranslateTransform>(path);
                move.Y = lift;
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { RepeatBehavior = RepeatBehavior.Forever, SpeedRatio = 1 });
            }
        }

        private static RadialGradientBrush AuraBrush(Ellipse aura)
        {
            if (aura.Fill is RadialGradientBrush brush && !brush.IsFrozen)
                return brush;
            brush = new RadialGradientBrush();
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.0));
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.62));
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
            aura.Fill = brush;
            return brush;
        }

        private static void TintAura(ButtonBase button, bool animate)
        {
            if (Part<Ellipse>(button, "OrbAura") is not Ellipse aura)
                return;
            bool lightTheme = ThemeColors.IsLightTheme;
            Color light = lightTheme
                ? Multron_Win_Cleaner.ScanButtonIconConverter.Accent(button.Content)
                : Multron_Win_Cleaner.ScanButtonIconConverter.Light(button.Content);
            var brush = AuraBrush(aura);
            var duration = TimeSpan.FromMilliseconds(animate ? 350 : 0);
            byte[] alphas = lightTheme ? new byte[] { 0xD8, 0xC0, 0x00 } : new byte[] { 0x70, 0x48, 0x00 };
            double[] offsets = lightTheme ? new[] { 0.0, 0.7, 1.0 } : new[] { 0.0, 0.62, 1.0 };
            for (int i = 0; i < brush.GradientStops.Count && i < alphas.Length; i++)
            {
                brush.GradientStops[i].Offset = offsets[i];
                brush.GradientStops[i].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(Color.FromArgb(alphas[i], light.R, light.G, light.B), duration));
            }
        }

        private static void Idle(ButtonBase button)
        {
            if (Part<Ellipse>(button, "OrbAura") is not Ellipse aura)
                return;
            var scale = Transform<ScaleTransform>(aura);
            string text = button.Content?.ToString() ?? "";
            bool scanning = button.IsEnabled && text == Loc.T("Cancel");
            bool waiting = button.IsEnabled && !scanning;
            if (!waiting)
            {
                aura.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)));
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                return;
            }
            bool lightTheme = ThemeColors.IsLightTheme;
            if (!Motion.Enabled)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                aura.BeginAnimation(UIElement.OpacityProperty, null);
                aura.Opacity = lightTheme ? 0.8 : 0.6;
                return;
            }
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            aura.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(lightTheme ? 0.6 : 0.35, lightTheme ? 1 : 0.85, TimeSpan.FromMilliseconds(1600))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                SpeedRatio = 1,
                EasingFunction = ease
            });
            var grow = new DoubleAnimation(0.94, 1.04, TimeSpan.FromMilliseconds(1600))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                SpeedRatio = 1,
                EasingFunction = ease
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }

        private static void OnMouse(object sender, System.Windows.Input.MouseEventArgs e) => Update(sender as ButtonBase);

        private static void OnEnabledStateChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is not ButtonBase button)
                return;
            Update(button);
            Idle(button);
        }

        private static void OnStateChanged(object? sender, EventArgs e) => Update(sender as ButtonBase);

        private static void OnContentChanged(object? sender, EventArgs e)
        {
            if (sender is ButtonBase button)
            {
                ApplyColors(button, true);
                TintAura(button, true);
                Idle(button);
            }
        }

        private static T? Part<T>(ButtonBase button, string name) where T : class =>
            button.Template?.FindName(name, button) as T;

        private static DropShadowEffect? Glow(ButtonBase button)
        {
            if (Part<Ellipse>(button, "CircleBg") is not Ellipse circle)
                return null;
            if (circle.Effect is DropShadowEffect glow && !glow.IsFrozen)
                return glow;
            glow = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Direction = 270, Opacity = 0.22 };
            circle.Effect = glow;
            return glow;
        }

        private static SolidColorBrush Brush(Shape shape)
        {
            if (shape.Stroke is SolidColorBrush brush && !brush.IsFrozen)
                return brush;
            brush = new SolidColorBrush(Colors.Transparent);
            shape.Stroke = brush;
            return brush;
        }

        private static T Transform<T>(UIElement element) where T : Transform, new()
        {
            if (element.RenderTransform is T transform && !transform.IsFrozen)
                return transform;
            transform = new T();
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = transform;
            return transform;
        }

        private static void ApplyColors(ButtonBase button, bool animate)
        {
            Color accent = Multron_Win_Cleaner.ScanButtonIconConverter.Accent(button.Content);
            Color light = Multron_Win_Cleaner.ScanButtonIconConverter.Light(button.Content);
            var duration = TimeSpan.FromMilliseconds(animate ? 350 : 0);
            if (Glow(button) is DropShadowEffect glow)
                glow.BeginAnimation(DropShadowEffect.ColorProperty, new ColorAnimation(accent, duration));
            if (Part<Ellipse>(button, "OrbHalo") is Ellipse halo)
                Brush(halo).BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(light, duration));
            if (Part<Ellipse>(button, "OrbRim") is Ellipse rim)
                Brush(rim).BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(light, duration));
            if (animate && button.IsMouseOver && button.IsEnabled)
                Ripple(button, 0.45, 1.22, 650);
        }

        private static void Animate(IAnimatable target, DependencyProperty property, double to, int milliseconds, IEasingFunction? easing = null) =>
            target.BeginAnimation(property, new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = easing });

        private static void Ripple(ButtonBase button, double opacity, double scale, int milliseconds)
        {
            if (Part<Ellipse>(button, "OrbHalo") is not Ellipse halo)
                return;
            var transform = Transform<ScaleTransform>(halo);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(milliseconds);
            halo.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(opacity, 0, duration) { EasingFunction = ease });
            var grow = new DoubleAnimation(1, scale, duration) { EasingFunction = ease };
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }

        private static void Update(ButtonBase? button)
        {
            if (button == null || Part<FrameworkElement>(button, "OrbRoot") is not FrameworkElement root)
                return;
            int state = !button.IsEnabled ? Disabled : button.IsPressed ? Pressed : button.IsMouseOver ? Hover : Normal;
            int previous = (int)button.GetValue(StateProperty);
            if (state == previous)
                return;
            button.SetValue(StateProperty, state);

            double scale = state switch { Hover => 1.06, Pressed => 0.96, _ => 1 };
            double blur = state switch { Hover => 26, Pressed => 10, _ => 14 };
            double depth = state switch { Hover => 2, Pressed => 1, _ => 3 };
            double glowOpacity = state switch { Hover => 0.38, Pressed => 0.3, Disabled => 0.08, _ => 0.22 };
            double rimOpacity = state switch { Hover => 0.55, Pressed => 0.35, _ => 0 };
            double lift = state switch { Hover => -2, Pressed => 1, _ => 0 };
            int time = state == Pressed ? 90 : 240;
            IEasingFunction ease = state == Hover
                ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }
                : new CubicEase { EasingMode = EasingMode.EaseOut };

            var rootScale = Transform<ScaleTransform>(root);
            Animate(rootScale, ScaleTransform.ScaleXProperty, scale, time, ease);
            Animate(rootScale, ScaleTransform.ScaleYProperty, scale, time, ease);

            if (Glow(button) is DropShadowEffect glow)
            {
                var soft = new CubicEase { EasingMode = EasingMode.EaseOut };
                Animate(glow, DropShadowEffect.BlurRadiusProperty, blur, time + 80, soft);
                Animate(glow, DropShadowEffect.ShadowDepthProperty, depth, time + 80, soft);
                Animate(glow, DropShadowEffect.OpacityProperty, glowOpacity, time + 80, soft);
            }
            if (Part<Ellipse>(button, "OrbRim") is Ellipse rim)
                Animate(rim, UIElement.OpacityProperty, rimOpacity, time + 60);
            if (Part<FrameworkElement>(button, "OrbGlyph") is FrameworkElement glyph)
                Animate(Transform<TranslateTransform>(glyph), TranslateTransform.YProperty, lift, time, ease);

            if (state == Hover && previous != Pressed)
                Ripple(button, 0.5, 1.2, 700);
            else if (state == Pressed)
                Ripple(button, 0.35, 1.1, 380);
        }
    }
}
