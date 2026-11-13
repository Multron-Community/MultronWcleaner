using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MultronWinCleaner.Processes
{
    public static class SearchPill
    {
        public static readonly DependencyProperty EnabledProperty =
            DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(SearchPill), new PropertyMetadata(false, OnEnabledChanged));

        public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

        public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

        private static void OnEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
        {
            if (element is not TextBox box || e.NewValue is not true)
                return;

            box.KeyDown += (s, args) =>
            {
                if (args.Key == Key.Escape && box.Text.Length > 0)
                {
                    box.Clear();
                    args.Handled = true;
                }
            };
            box.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((s, args) =>
            {
                if ((args.OriginalSource as FrameworkElement)?.Name != "PART_Clear")
                    return;
                box.Clear();
                box.Focus();
                args.Handled = true;
            }));
        }
    }
}
