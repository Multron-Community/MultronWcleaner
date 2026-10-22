using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using MultronWinCleaner.Processes;

namespace MultronWinCleaner
{
    public partial class CloudScanView : UserControl
    {
        public static readonly DependencyProperty ListMaxHeightProperty =
            DependencyProperty.Register(nameof(ListMaxHeight), typeof(double), typeof(CloudScanView), new PropertyMetadata(240.0));

        public double ListMaxHeight
        {
            get => (double)GetValue(ListMaxHeightProperty);
            set => SetValue(ListMaxHeightProperty, value);
        }

        public CloudScanView()
        {
            InitializeComponent();
            var tracker = CloudUploadTracker.Instance;
            DataContext = tracker;
            tracker.PropertyChanged += (s, e) => UpdateVisibility();
            UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            var tracker = CloudUploadTracker.Instance;
            Visibility = tracker.IsScanning ? Visibility.Visible : Visibility.Collapsed;
            UploadsBox.Visibility = tracker.HasUploads ? Visibility.Visible : Visibility.Collapsed;
            MoreLabel.Visibility = string.IsNullOrEmpty(tracker.MoreText) ? Visibility.Collapsed : Visibility.Visible;
            ResetLink.Visibility = tracker.IsFiltered ? Visibility.Visible : Visibility.Collapsed;
            EmptyLabel.Visibility = string.IsNullOrEmpty(tracker.EmptyText) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ResetFilter_Click(object sender, RoutedEventArgs e) => CloudUploadTracker.Instance.ResetFilter();

        private void Threats_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => CloudUploadTracker.Instance.RequestThreats();

        private void CancelUpload_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Hyperlink)?.DataContext is CloudUploadItem item)
                item.RequestCancel();
        }
    }
}
