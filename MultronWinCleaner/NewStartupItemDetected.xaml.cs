using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MultronWinCleaner
{
    public partial class NewStartupItemDetected : Window
    {
        StartupManager startupManager;
        private readonly string appName;
        private readonly string appPath;

        public NewStartupItemDetected(string app, string path, StartupManager startupManager)
        {
            InitializeComponent();
            this.ItemNameText.Text = app;
            this.ItemPathText.Text = path;
            this.startupManager = startupManager;
            appName = app;
            appPath = path;
            this.Topmost = true;
            Loaded += NewStartupItemDetected_Loaded;
        }

        private void NewStartupItemDetected_Loaded(object sender, RoutedEventArgs e)
        {
            var workingArea = SystemParameters.WorkArea;
            this.Left = workingArea.Right - this.ActualWidth - 10;  
            this.Top = workingArea.Bottom - this.ActualHeight - 10; 
        }

        private void AllowButton_Click(object sender, RoutedEventArgs e)
        {
            startupManager.RevealItem(appName, appPath);
            this.Close();
        }

        private void DisableButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
