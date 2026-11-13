using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MultronWinCleaner.Processes
{
    public static class CardLayout
    {
        public static readonly Color Accent = Color.FromRgb(0x42, 0xA5, 0xF5);
        public static readonly Color Green = Color.FromRgb(0x2E, 0xB8, 0x72);

        public static Grid CreateGrid(Thickness margin)
        {
            var grid = new Grid { Margin = margin };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return grid;
        }

        public static void Add(Grid grid, FrameworkElement card, int index = -1)
        {
            if (index < 0 || index >= grid.Children.Count)
                grid.Children.Add(card);
            else
                grid.Children.Insert(index, card);
            DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, card.GetType()).AddValueChanged(card, (s, e) => Layout(grid));
            if (card is Expander expander)
            {
                expander.Expanded += (s, e) =>
                {
                    if (!ReferenceEquals(e.OriginalSource, expander))
                        return;
                    AlignRows(grid);
                    AnimateOpen(expander);
                };
                expander.Collapsed += (s, e) =>
                {
                    if (!ReferenceEquals(e.OriginalSource, expander))
                        return;
                    double from = expander.ActualHeight;
                    double to = (double)expander.GetValue(ClosedRowHeightProperty);
                    if (AnimateClose(grid, expander, from, to))
                        return;
                    AlignRows(grid);
                };
            }
            card.SizeChanged += (s, e) =>
            {
                if (!RowOpen(grid, Grid.GetRow(card)))
                    card.SetValue(ClosedRowHeightProperty, card.ActualHeight);
            };
            Layout(grid);
        }

        private static readonly Duration CardAnimation = new Duration(TimeSpan.FromMilliseconds(220));

        private static bool AnimationsOn => Motion.Enabled;

        public static void AnimateOpen(Expander card)
        {
            if (!AnimationsOn || !card.IsLoaded || card.Content is not FrameworkElement content)
                return;
            card.BeginAnimation(FrameworkElement.HeightProperty, null);
            var scale = new ScaleTransform(1, 0);
            content.LayoutTransform = scale;
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            var grow = new System.Windows.Media.Animation.DoubleAnimation(0, 1, CardAnimation) { EasingFunction = ease };
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, CardAnimation) { EasingFunction = ease, FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop };
            grow.Completed += (s, e) =>
            {
                if (ReferenceEquals(content.LayoutTransform, scale))
                    content.ClearValue(FrameworkElement.LayoutTransformProperty);
                content.BeginAnimation(UIElement.OpacityProperty, null);
                content.ClearValue(UIElement.OpacityProperty);
            };
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            content.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private static readonly DependencyProperty ClosingProperty =
            DependencyProperty.RegisterAttached("Closing", typeof(bool), typeof(CardLayout), new PropertyMetadata(false));

        private static bool IsOpen(FrameworkElement card) => card is Expander { IsExpanded: true } || (bool)card.GetValue(ClosingProperty);

        private static bool AnimateClose(Grid grid, Expander card, double from, double to)
        {
            if (!AnimationsOn || !card.IsLoaded || from <= 0 || to <= 0 || from <= to + 1)
                return false;
            card.SetValue(ClosingProperty, true);
            card.VerticalAlignment = VerticalAlignment.Top;
            var shrink = new System.Windows.Media.Animation.DoubleAnimation(from, to, CardAnimation)
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
                FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
            };
            shrink.Completed += (s, e) =>
            {
                card.SetValue(ClosingProperty, false);
                AlignRows(grid);
            };
            card.BeginAnimation(FrameworkElement.HeightProperty, shrink);
            return true;
        }

        private static readonly DependencyProperty ClosedRowHeightProperty =
            DependencyProperty.RegisterAttached("ClosedRowHeight", typeof(double), typeof(CardLayout), new PropertyMetadata(0.0));

        private static bool RowOpen(Grid grid, int row) =>
            grid.Children.OfType<FrameworkElement>().Any(c => c.Visibility != Visibility.Collapsed && Grid.GetRow(c) == row && IsOpen(c));

        public static void AlignRows(Grid grid)
        {
            foreach (var row in grid.Children.OfType<FrameworkElement>().Where(c => c.Visibility != Visibility.Collapsed).GroupBy(Grid.GetRow))
            {
                var cards = row.ToList();
                if (!cards.Any(IsOpen))
                {
                    foreach (var card in cards)
                    {
                        card.VerticalAlignment = VerticalAlignment.Stretch;
                        card.ClearValue(FrameworkElement.MinHeightProperty);
                    }
                    continue;
                }
                double closedHeight = cards.Max(c => (double)c.GetValue(ClosedRowHeightProperty));
                foreach (var card in cards)
                {
                    card.VerticalAlignment = VerticalAlignment.Top;
                    if (IsOpen(card) || closedHeight <= 0)
                        card.ClearValue(FrameworkElement.MinHeightProperty);
                    else
                        card.MinHeight = closedHeight;
                }
            }
        }

        public static void Layout(Grid grid)
        {
            int index = 0;
            foreach (UIElement child in grid.Children)
            {
                if (child.Visibility == Visibility.Collapsed)
                    continue;
                if (Grid.GetColumnSpan(child) > 1)
                {
                    if (index % 2 == 1)
                        index++;
                    Grid.SetRow(child, index / 2);
                    Grid.SetColumn(child, 0);
                    index += 2;
                    continue;
                }
                Grid.SetRow(child, index / 2);
                Grid.SetColumn(child, index % 2);
                index++;
            }
            int rows = Math.Max(1, (index + 1) / 2);
            while (grid.RowDefinitions.Count < rows)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            while (grid.RowDefinitions.Count > rows)
                grid.RowDefinitions.RemoveAt(grid.RowDefinitions.Count - 1);
            AlignRows(grid);
        }

        private static readonly Brush IconBackground = CreateIconBackground();
        private static readonly Brush IconEdge = CreateFrozen(new SolidColorBrush(Color.FromArgb(0x2B, 0x78, 0xDC, 0xE6)));

        private static Brush CreateIconBackground()
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x14, 0x62, 0x6D), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x0B, 0x4A, 0x54), 0.5));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x01, 0x18, 0x1F), 1));
            return CreateFrozen(brush);
        }

        private static readonly System.Windows.Media.Effects.DropShadowEffect IconShadow = CreateIconShadow();

        private static System.Windows.Media.Effects.DropShadowEffect CreateIconShadow()
        {
            var shadow = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 5, ShadowDepth = 1.5, Direction = 270, Opacity = 0.35, Color = Colors.Black };
            shadow.Freeze();
            return shadow;
        }

        private static Brush CreateFrozen(Brush brush)
        {
            brush.Freeze();
            return brush;
        }

        public static Border Icon(string glyph)
        {
            return new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(9),
                Margin = new Thickness(0, 2, 12, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Background = IconBackground,
                BorderBrush = IconEdge,
                BorderThickness = new Thickness(1),
                Effect = IconShadow,
                Child = new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 17,
                    FontWeight = FontWeights.Normal,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        public static void MakeSelectable(Expander card, FrameworkElement header, Action onClick)
        {
            card.SetResourceReference(FrameworkElement.StyleProperty, "SelectCardExpanderStyle");
            card.HorizontalAlignment = HorizontalAlignment.Stretch;
            card.VerticalAlignment = VerticalAlignment.Stretch;
            card.Margin = new Thickness(5);
            card.BorderThickness = new Thickness(1);
            header.Cursor = Cursors.Hand;
            if (header is Panel panel && panel.Background == null)
                panel.Background = Brushes.Transparent;
            header.MouseLeftButtonUp += (s, e) =>
            {
                if (e.OriginalSource is DependencyObject source && IsInsideButton(source, header))
                    return;
                onClick();
                e.Handled = true;
            };
            SetSelected(card, false);
        }

        private static bool IsInsideButton(DependencyObject node, DependencyObject stop)
        {
            for (DependencyObject? d = node; d != null && d != stop; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
                if (d is System.Windows.Controls.Primitives.ButtonBase)
                    return true;
            return false;
        }

        public static void SetSelected(Expander card, bool selected)
        {
            if (selected)
            {
                card.Background = new SolidColorBrush(Color.FromArgb(0x12, 0x1E, 0x88, 0xE5));
                card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x5C, 0x1E, 0x88, 0xE5));
            }
            else
            {
                card.Background = new SolidColorBrush(Color.FromArgb(0x0F, 0x80, 0x80, 0x80));
                card.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            }
        }

        public static (Border Pill, TextBlock Text) Pill()
        {
            var text = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold };
            var pill = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 2, 8, 2), Child = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            return (pill, text);
        }

        public static void SetPill(Border pill, TextBlock text, bool on, string? tooltip)
        {
            text.Text = on ? Loc.T("On") : Loc.T("Off");
            pill.ToolTip = tooltip;
            if (on)
            {
                text.SetResourceReference(TextBlock.ForegroundProperty, "ResultSuccessText");
                pill.Background = new SolidColorBrush(Color.FromArgb(0x1F, Green.R, Green.G, Green.B));
            }
            else
            {
                text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                pill.Background = new SolidColorBrush(Color.FromArgb(0x1F, 0x80, 0x80, 0x80));
            }
        }
    }
}
