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

        public ToolSettingsWindow(Window owner, string title, FrameworkElement content, double width)
        {
            InitializeComponent();
            Owner = owner;
            var workArea = SystemParameters.WorkArea;
            Width = Math.Min(width, workArea.Width * 0.95);
            Height = Math.Min(PreferredHeight, workArea.Height * 0.85);
            MaxWidth = workArea.Width;
            MaxHeight = workArea.Height;
            Title = title;
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
    }
}
