using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MultronWinCleaner
{
    public partial class ToolSettingsWindow : Window
    {
        private const double PreferredHeight = 600;

        private bool ownerClosed;

        public ToolSettingsWindow(Window owner, string title, FrameworkElement content, double width, double height = PreferredHeight, bool windowButtons = false)
        {
            InitializeComponent();
            Owner = owner;
            string iconName = title == Loc.T("Cleaning Selections") ? "selections" : title == Loc.T("Folders, Drives and Files") || title == Loc.T("Found Files") ? "" : "settings";
            if (iconName.Length > 0)
                Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Icons/mwc_" + iconName + ".ico"));
            else if (owner.Icon != null)
                Icon = owner.Icon;
            var workArea = SystemParameters.WorkArea;
            Width = Math.Min(width, workArea.Width * 0.95);
            Height = Math.Min(height, workArea.Height * 0.85);
            MaxWidth = workArea.Width;
            MaxHeight = workArea.Height;
            Title = title;
            if (windowButtons)
            {
                MinimizeWindowButton.Visibility = Visibility.Visible;
                MaximizeWindowButton.Visibility = Visibility.Visible;
                ShowInTaskbar = true;
            }
            TitleText.Text = title;
            DataContext = owner.DataContext;
            Resources.MergedDictionaries.Add(owner.Resources);
            NameScope.SetNameScope(this, NameScope.GetNameScope(owner));

            switch (content.Parent)
            {
                case Panel panel:
                    panel.Children.Remove(content);
                    break;
                case Decorator decorator:
                    decorator.Child = null;
                    break;
                case ContentControl control:
                    control.Content = null;
                    break;
            }
            ContentHost.Content = content;

            owner.Closed += (s, e) =>
            {
                ownerClosed = true;
                Close();
            };
            owner.IsVisibleChanged += (s, e) =>
            {
                if (!owner.IsVisible)
                    Hide();
            };
        }

        public void SetFooter(FrameworkElement footer) => FooterHost.Content = footer;

        public void ShowOrActivate()
        {
            if (!IsVisible)
                Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!ownerClosed)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnClosing(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        private void TopBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private Rect? restoreBounds;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (restoreBounds is Rect bounds)
            {
                Left = bounds.Left;
                Top = bounds.Top;
                Width = bounds.Width;
                Height = bounds.Height;
                restoreBounds = null;
                return;
            }
            restoreBounds = new Rect(Left, Top, Width, Height);
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left;
            Top = workArea.Top;
            Width = workArea.Width;
            Height = workArea.Height;
        }
    }
}
