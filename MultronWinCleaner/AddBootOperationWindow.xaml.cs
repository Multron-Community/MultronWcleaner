using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MultronWinCleaner.Processes;

namespace MultronWinCleaner
{
    public partial class AddBootOperationWindow : Window
    {
        private readonly ObservableCollection<BootOperation> operations = new();

        public AddBootOperationWindow()
        {
            InitializeComponent();
            foreach (var operation in BootOperations.Load())
                operations.Add(operation);
            lstBootOperations.ItemsSource = operations;
            operations.CollectionChanged += (_, _) => RefreshList();
            cmbOperationType.SelectedIndex = 0;
            RefreshList();
        }

        private BootOperationType SelectedType => (BootOperationType)Math.Max(0, cmbOperationType.SelectedIndex);

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void CmbOperationType_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateFields();

        private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

        private void UpdateFields()
        {
            var type = SelectedType;
            bool preset = BootOperations.IsPreset(type);
            bool program = type == BootOperationType.RunProgram;
            bool emptyFolder = type == BootOperationType.EmptyFolder;
            bool manual = type == BootOperationType.ManualCommand;
            bool needsDestination = type == BootOperationType.Move || type == BootOperationType.Rename;

            SourcePanel.Visibility = Show(!manual && !preset);
            SourceFileButton.Visibility = Show(!emptyFolder);
            SourceFolderButton.Visibility = Show(!program);
            SourceLabel.Text = Loc.T(program ? "Program" : emptyFolder ? "Folder" : "File or folder");
            DestinationPanel.Visibility = Show(needsDestination);
            DestinationBrowseButton.Visibility = Show(type == BootOperationType.Move);
            DestinationLabel.Text = Loc.T(type == BootOperationType.Rename ? "New name or full path" : "Destination");
            CommandPanel.Visibility = Show(manual || program || preset);
            CommandLabel.Text = Loc.T(program ? "Arguments (optional)" : "Command");

            if (preset)
                txtManualCommand.Text = BootOperations.PresetCommand(type);
            else if (txtManualCommand.IsReadOnly)
                txtManualCommand.Clear();
            txtManualCommand.IsReadOnly = preset;
        }

        private void RefreshList()
        {
            EmptyListText.Visibility = operations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            lstBootOperations.Visibility = operations.Count == 0 ? Visibility.Hidden : Visibility.Visible;
        }

        private void SetStatus(string text, bool problem = false)
        {
            StatusText.Text = text;
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, problem ? "UtilityDangerText" : "TextSecondary");
        }

        private void BrowseSourceFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = Loc.T("Select a file"), CheckFileExists = true };
            if (dialog.ShowDialog(this) == true)
                txtSourcePath.Text = dialog.FileName;
        }

        private void BrowseSourceFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("Select a folder") };
            if (dialog.ShowDialog(this) == true)
                txtSourcePath.Text = dialog.FolderName;
        }

        private void BrowseDestination_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("Select a folder") };
            if (dialog.ShowDialog(this) == true)
                txtDestPath.Text = dialog.FolderName;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            var operation = BootOperations.Create(SelectedType, txtSourcePath.Text, txtDestPath.Text, txtManualCommand.Text, out string error, out bool insideWindows);
            if (operation == null)
            {
                AppDialog.Show(this, error, Loc.T("Input Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (operations.Any(existing => existing.SameAs(operation)))
            {
                AppDialog.Show(this, Loc.T("This operation is already in the list."), Loc.T("Input Error"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (insideWindows && AppDialog.Show(this, Loc.T("This item is inside the Windows folder. Changing it can stop Windows from starting. Continue?"), Loc.T("Add Boot Operation"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            operations.Add(operation);
            lstBootOperations.SelectedItem = operation;
            txtSourcePath.Clear();
            txtDestPath.Clear();
            txtManualCommand.Clear();
            UpdateFields();
            SetStatus(string.Empty);
        }

        private void Schedule_Click(object sender, RoutedEventArgs e)
        {
            var pending = operations.Where(o => o.State != BootOperationState.Scheduled).ToList();
            if (pending.Count == 0)
            {
                SetStatus(Loc.T("There are no waiting operations to schedule."));
                return;
            }

            int failed = 0;
            string lastError = string.Empty;
            foreach (var operation in pending)
            {
                if (BootOperations.Schedule(operation, out string error))
                    continue;
                failed++;
                lastError = error;
            }
            BootOperations.Save(operations);

            if (failed > 0)
                SetStatus(Loc.F("{0} of {1} operation(s) could not be scheduled: {2}", failed, pending.Count, lastError), true);
            else
                SetStatus(Loc.F("{0} operation(s) scheduled. They run at the next restart.", pending.Count));
        }

        private async void RunNow_Click(object sender, RoutedEventArgs e)
        {
            if (lstBootOperations.SelectedItem is not BootOperation selected)
            {
                AppDialog.Show(this, Loc.T("Please select an operation from the list."), Loc.T("Run Selected Now"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (selected.State == BootOperationState.Scheduled)
            {
                AppDialog.Show(this, Loc.T("This operation is already scheduled. Remove it from the list first to run it now."), Loc.T("Run Selected Now"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (AppDialog.Show(this, Loc.F("Run this operation now?\n{0}", selected.DetailsText), Loc.T("Run Selected Now"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            RunNowButton.IsEnabled = false;
            var result = await BootOperations.RunNowAsync(selected);
            RunNowButton.IsEnabled = true;

            if (result.Ok)
            {
                operations.Remove(selected);
                SetStatus(result.Message);
            }
            else
            {
                selected.State = BootOperationState.Failed;
                SetStatus(Loc.F("Error while running the command: {0}", result.Message), true);
            }
        }

        private void RemoveOperation_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not BootOperation operation)
                return;
            if (operation.State == BootOperationState.Scheduled && !BootOperations.Cancel(operation))
            {
                SetStatus(Loc.T("Could not cancel the scheduled operation."), true);
                return;
            }
            operations.Remove(operation);
            BootOperations.Save(operations);
            SetStatus(string.Empty);
        }
    }
}
