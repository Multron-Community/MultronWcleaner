using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using MultronWinCleaner.Processes;

namespace MultronWinCleaner
{
    public partial class ThreatsWindow : Window
    {
        private readonly ObservableCollection<CloudScanResult> source;
        private readonly ListCollectionView view;

        public ThreatsWindow(ObservableCollection<CloudScanResult> results)
        {
            InitializeComponent();
            source = results;
            view = new ListCollectionView(results) { Filter = o => o is CloudScanResult r && r.Verdict is "malicious" or "suspicious" };
            ThreatsGrid.ItemsSource = view;
            source.CollectionChanged += Source_CollectionChanged;
            UpdateCount();
        }

        private void Source_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateCount();

        private void UpdateCount()
        {
            int count = view.Count;
            SubtitleText.Text = Loc.F("{0} threat(s) found. Choose what to do with them in the scan results after the scan finishes.", count);
            EmptyText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private CloudScanResult? Selected => ThreatsGrid.SelectedItem as CloudScanResult;

        private void OpenLocation_Click(object sender, RoutedEventArgs e)
        {
            var item = Selected;
            if (item == null || !File.Exists(item.FilePath)) return;
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.FilePath}\"") { UseShellExecute = true });
        }

        private void CopyPath_Click(object sender, RoutedEventArgs e)
        {
            var item = Selected;
            if (item != null) Clipboard.SetText(item.FilePath);
        }

        private void TopBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            source.CollectionChanged -= Source_CollectionChanged;
            view.DetachFromSourceCollection();
            base.OnClosed(e);
        }
    }
}
