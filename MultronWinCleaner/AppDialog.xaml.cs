using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MultronWinCleaner
{
    public partial class AppDialog : Window
    {
        private MessageBoxResult result;
        private MessageBoxResult cancelResult;

        private AppDialog()
        {
            InitializeComponent();
        }

        public static MessageBoxResult Show(string message) =>
            Show(null, message, "", MessageBoxButton.OK, MessageBoxImage.None);

        public static MessageBoxResult Show(string message, string title) =>
            Show(null, message, title, MessageBoxButton.OK, MessageBoxImage.None);

        public static MessageBoxResult Show(string message, string title, MessageBoxButton buttons) =>
            Show(null, message, title, buttons, MessageBoxImage.None);

        public static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image) =>
            Show(null, message, title, buttons, image);

        public static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult) =>
            Show(null, message, title, buttons, image);

        public static MessageBoxResult Show(Window? owner, string message, string title, MessageBoxButton buttons, MessageBoxImage image)
        {
            var app = Application.Current;
            if (app == null)
                return MessageBox.Show(message, title, buttons, image);
            if (!app.Dispatcher.CheckAccess())
                return app.Dispatcher.Invoke(() => Show(owner, message, title, buttons, image));

            var dialog = new AppDialog();
            dialog.Setup(message, title, buttons, image);
            dialog.PlaceOver(owner ?? ActiveWindow());
            dialog.ShowDialog();
            return dialog.result;
        }

        public static string? Prompt(Window? owner, string message, string title, int maxLength)
        {
            var app = Application.Current;
            if (app == null)
                return null;
            if (!app.Dispatcher.CheckAccess())
                return app.Dispatcher.Invoke(() => Prompt(owner, message, title, maxLength));

            var dialog = new AppDialog();
            dialog.Setup(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question);
            dialog.InputBox.Visibility = Visibility.Visible;
            dialog.InputBox.MaxLength = maxLength;
            dialog.Loaded += (s, e) => dialog.InputBox.Focus();
            dialog.PlaceOver(owner ?? ActiveWindow());
            dialog.ShowDialog();
            return dialog.result == MessageBoxResult.OK ? dialog.InputBox.Text.Trim() : null;
        }

        private static Window? ActiveWindow()
        {
            var windows = Application.Current.Windows.OfType<Window>().Where(w => w.IsVisible && w.WindowState != WindowState.Minimized && w is not AppDialog).ToList();
            return windows.FirstOrDefault(w => w.IsActive) ?? (Application.Current.MainWindow is Window main && windows.Contains(main) ? main : windows.LastOrDefault());
        }

        private void Setup(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
        {
            Title = title.Length > 0 ? title : Loc.T("Multron Win Cleaner");
            TitleText.Text = Title;
            MessageText.Text = message;

            if (image == MessageBoxImage.None)
                image = buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? MessageBoxImage.Question : MessageBoxImage.Information;
            (string icon, string color) = image switch
            {
                MessageBoxImage.Error => ("", "#DC3545"),
                MessageBoxImage.Warning => ("", "#FF9800"),
                MessageBoxImage.Question => ("", "#1E88E5"),
                _ => ("", "#1E88E5")
            };
            var iconColor = (Color)ColorConverter.ConvertFromString(color);
            IconText.Text = icon;
            IconText.Foreground = new SolidColorBrush(iconColor);
            IconBorder.Background = new SolidColorBrush(Color.FromArgb(0x20, iconColor.R, iconColor.G, iconColor.B));

            switch (buttons)
            {
                case MessageBoxButton.OK:
                    AddButton(Loc.T("OK"), MessageBoxResult.OK, primary: true);
                    cancelResult = MessageBoxResult.OK;
                    break;
                case MessageBoxButton.OKCancel:
                    AddButton(Loc.T("Cancel"), MessageBoxResult.Cancel, primary: false);
                    AddButton(Loc.T("OK"), MessageBoxResult.OK, primary: true);
                    cancelResult = MessageBoxResult.Cancel;
                    break;
                case MessageBoxButton.YesNo:
                    AddButton(Loc.T("No"), MessageBoxResult.No, primary: false);
                    AddButton(Loc.T("Yes"), MessageBoxResult.Yes, primary: true);
                    cancelResult = MessageBoxResult.No;
                    break;
                case MessageBoxButton.YesNoCancel:
                    AddButton(Loc.T("Cancel"), MessageBoxResult.Cancel, primary: false);
                    AddButton(Loc.T("No"), MessageBoxResult.No, primary: false);
                    AddButton(Loc.T("Yes"), MessageBoxResult.Yes, primary: true);
                    cancelResult = MessageBoxResult.Cancel;
                    break;
            }
            result = cancelResult;
        }

        private void AddButton(string text, MessageBoxResult value, bool primary)
        {
            var button = new Button
            {
                Content = text,
                Padding = new Thickness(20, 8, 20, 8),
                Margin = new Thickness(8, 0, 0, 0),
                MinWidth = 80,
                Cursor = Cursors.Hand,
                IsDefault = primary
            };
            if (TryFindResource(primary ? "AccentButtonStyle" : "ModernButtonStyle") is Style style)
                button.Style = style;
            if (!primary)
            {
                button.Background = Brushes.Transparent;
                button.BorderThickness = new Thickness(1);
                button.BorderBrush = Brushes.Gray;
                button.SetResourceReference(ForegroundProperty, "TextPrimary");
            }
            button.Click += (s, e) =>
            {
                result = value;
                Close();
            };
            ButtonsPanel.Children.Add(button);
            if (primary)
                Loaded += (s, e) => { if (InputBox.Visibility != Visibility.Visible) button.Focus(); };
        }

        private void PlaceOver(Window? owner)
        {
            if (owner != null && owner.IsVisible && owner.WindowState != WindowState.Minimized && owner.ActualWidth > 200 && owner.ActualHeight > 150)
            {
                Owner = owner;
                if (owner.WindowState == WindowState.Maximized)
                {
                    var area = SystemParameters.WorkArea;
                    Left = area.Left; Top = area.Top; Width = area.Width; Height = area.Height;
                }
                else
                {
                    Left = owner.Left; Top = owner.Top; Width = owner.ActualWidth; Height = owner.ActualHeight;
                }
                return;
            }

            Dimmer.Background = Brushes.Transparent;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                result = cancelResult;
                Close();
            }
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && MessageText.SelectionLength == 0)
            {
                try { Clipboard.SetText(TitleText.Text + Environment.NewLine + Environment.NewLine + MessageText.Text); } catch { }
            }
        }
    }
}
