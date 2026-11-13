using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MultronWinCleaner.Processes
{
    public static class RoundedClip
    {
        public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(RoundedClip), new PropertyMetadata(false, OnEnabledChanged));

        public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

        public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

        private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Border border || e.NewValue is not true)
                return;
            border.SizeChanged += (s, a) => Apply(border);
            border.Loaded += (s, a) => Apply(border);
            DependencyPropertyDescriptor.FromProperty(Border.CornerRadiusProperty, typeof(Border)).AddValueChanged(border, (s, a) => Apply(border));
            Apply(border);
        }

        private static void Apply(Border border)
        {
            if (border.Child is not FrameworkElement child)
                return;
            double inset = border.BorderThickness.Left;
            double width = border.ActualWidth - border.BorderThickness.Left - border.BorderThickness.Right;
            double height = border.ActualHeight - border.BorderThickness.Top - border.BorderThickness.Bottom;
            double radius = System.Math.Max(0, border.CornerRadius.TopLeft - inset);
            if (width <= 0 || height <= 0 || radius <= 0)
            {
                child.Clip = null;
                return;
            }
            var clip = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
            clip.Freeze();
            child.Clip = clip;
        }
    }
}
