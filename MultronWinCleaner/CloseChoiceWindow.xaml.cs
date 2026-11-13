using System.Windows;
using System.Windows.Input;

namespace MultronWinCleaner
{
    public partial class CloseChoiceWindow : Window
    {
        public enum Choice
        {
            Cancel,
            Hide,
            Exit
        }

        private Choice result = Choice.Cancel;

        private CloseChoiceWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => HideButton.Focus();
        }

        public static (Choice Result, bool Remember) Ask(Window? owner)
        {
            var window = new CloseChoiceWindow();
            if (owner != null && owner.IsVisible && owner.WindowState != WindowState.Minimized)
            {
                window.Owner = owner;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                window.Topmost = true;
            }
            window.ShowDialog();
            return (window.result, window.result != Choice.Cancel && window.RememberBox.IsChecked == true);
        }

        private void Finish(Choice choice)
        {
            result = choice;
            Close();
        }

        private void Hide_Click(object sender, RoutedEventArgs e) => Finish(Choice.Hide);

        private void Exit_Click(object sender, RoutedEventArgs e) => Finish(Choice.Exit);

        private void Cancel_Click(object sender, RoutedEventArgs e) => Finish(Choice.Cancel);

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                Finish(Choice.Cancel);
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
    }
}
