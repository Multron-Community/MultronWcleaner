using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MultronWinCleaner.Processes;

namespace MultronWinCleaner
{
    public class MrtResultsWindow : Window
    {
        public MrtResultsWindow(Window owner, MrtScanner.Result result, string color, Action? removeWithMrt, Action openLog)
        {
            Owner = owner;
            Title = Loc.T("Microsoft MRT Results");
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Icons/mwc_malware.ico"));
            Width = 720;
            Height = 560;
            MinWidth = 480;
            MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SetResourceReference(BackgroundProperty, "WindowBackground");
            SetResourceReference(ForegroundProperty, "TextPrimary");

            var root = new Grid { Margin = new Thickness(20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            var status = new TextBlock
            {
                Text = result.Message,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = brush,
                TextWrapping = TextWrapping.Wrap
            };
            root.Children.Add(status);

            string details = Loc.F("Scan type: {0}", Loc.T(result.FullScan ? "Full Scan" : "Quick Scan"))
                + "   ·   " + Loc.F("Duration: {0}", result.Elapsed.ToString(@"hh\:mm\:ss"))
                + (result.ExitCode >= 0 ? "   ·   " + Loc.F("Exit code: {0}", result.ExitCode) : "");
            var info = new TextBlock { Text = details, FontSize = 12, Opacity = 0.7, Margin = new Thickness(0, 6, 0, 14), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(info, 1);
            root.Children.Add(info);

            var found = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            var lines = result.Summary.Where(l => !l.Contains("No infection found", StringComparison.OrdinalIgnoreCase)).ToList();
            found.Children.Add(new TextBlock
            {
                Text = result.Outcome == MrtScanner.Outcome.Threats ? Loc.T("Found by Microsoft MRT:") : Loc.T("Result:"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });
            if (lines.Count == 0)
                lines.Add(result.Outcome == MrtScanner.Outcome.Clean ? Loc.T("No malicious software was found.") : Loc.T("Details are in mrt.log."));
            foreach (string line in lines)
            {
                var row = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 2, 0, 2),
                    Child = new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 }
                };
                row.SetResourceReference(Border.BackgroundProperty, "SecondaryPanelBackground");
                found.Children.Add(row);
            }
            Grid.SetRow(found, 2);
            root.Children.Add(found);

            var report = new Expander
            {
                Header = Loc.T("Full report from mrt.log"),
                IsExpanded = result.Outcome != MrtScanner.Outcome.Clean,
                Content = new TextBox
                {
                    Text = result.LogText.Length > 0 ? result.LogText : Loc.T("mrt.log has no report for this scan."),
                    IsReadOnly = true,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.NoWrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Margin = new Thickness(0, 6, 0, 0),
                    Padding = new Thickness(6)
                }
            };
            report.SetResourceReference(ForegroundProperty, "TextPrimary");
            ((TextBox)report.Content).SetResourceReference(BackgroundProperty, "TextBoxBackground");
            ((TextBox)report.Content).SetResourceReference(ForegroundProperty, "TextPrimary");
            Grid.SetRow(report, 3);
            root.Children.Add(report);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            if (removeWithMrt != null && result.CanRemove)
            {
                var remove = MakeButton(Loc.T("Remove with MRT"), "#FFDC3545");
                remove.ToolTip = Loc.T("Runs Microsoft MRT again and lets it remove the malicious software it finds.");
                remove.Click += (s, e) => { Close(); removeWithMrt(); };
                buttons.Children.Add(remove);
            }
            var openLogButton = MakeButton(Loc.T("Open mrt.log"), null);
            openLogButton.Click += (s, e) => openLog();
            buttons.Children.Add(openLogButton);
            var close = MakeButton(Loc.T("Close"), null);
            close.Click += (s, e) => Close();
            buttons.Children.Add(close);
            Grid.SetRow(buttons, 4);
            root.Children.Add(buttons);

            Content = root;
        }

        private static Button MakeButton(string text, string? background)
        {
            var button = new Button
            {
                Content = text,
                Padding = new Thickness(16, 7, 16, 7),
                Margin = new Thickness(8, 0, 0, 0),
                MinWidth = 90,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            if (background != null)
            {
                button.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(background));
                button.Foreground = Brushes.White;
            }
            return button;
        }
    }
}
