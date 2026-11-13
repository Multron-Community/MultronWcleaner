using Microsoft.Win32;
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
    public partial class AddStartup : Window
    {
        public AddStartup()
        {
            InitializeComponent();
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                this.DragMove();
        }
        public static void AddWinlogonEntry(string pathToAdd)
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey( @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", writable: true);

                if (key == null)
                {
                    AppDialog.Show(Loc.T("Key returned null."), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                } else
                {
                    string raw = key.GetValue("Userinit") as string ?? "";


                    var parts = raw.Split(',').Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToList();

                    bool alreadyExists = parts.Any(p => p.Equals(pathToAdd, StringComparison.OrdinalIgnoreCase));
                    if (alreadyExists)
                    {
                        AppDialog.Show(Loc.F("{0} already exists", pathToAdd), Loc.T("Warning"), MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    else
                    {
                        parts.Add(pathToAdd);

                        string newValue = string.Join(",", parts) + ",";
                        key.SetValue("Userinit", newValue);
                        AppDialog.Show(Loc.F("Operation successful! {0} > added!", newValue), Loc.T("Success"), MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }

           

            

            }
            catch (Exception ex) { 
                 AppDialog.Show(ex.Message, Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            
            }
        }
        private void Add_Click(object sender, RoutedEventArgs e)
        {
            string name = txtName.Text.Trim();
            string path = txtPath.Text.Trim();

            if (string.IsNullOrEmpty(name) && rbWinLogon.IsChecked != true)
            {
                AppDialog.Show(Loc.T("Please enter a name."), Loc.T("Input Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(path))
            {
                AppDialog.Show(Loc.T("Please enter the executable path."), Loc.T("Input Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (rbRegistry.IsChecked == true)
                {
                  
                    using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);

                    if (runKey == null)
                    {
                        AppDialog.Show(Loc.T("Unable to open registry key."), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    runKey.SetValue(name, $"\"{path}\"");
                    AppDialog.Show(Loc.T("Startup application added successfully. "), Loc.T("Success"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (rbTaskScheduler.IsChecked == true)
                { 
                    string args = $"/create /tn \"{name}\" /tr \"\\\"{path}\\\"\" /sc onlogon /rl highest /f";

                    var proc = new System.Diagnostics.Process();
                    proc.StartInfo.FileName = "schtasks.exe";
                    proc.StartInfo.Arguments = args;
                    proc.StartInfo.UseShellExecute = false;
                    proc.StartInfo.CreateNoWindow = true;
                    proc.Start();
                    proc.WaitForExit();

                    if (proc.ExitCode != 0)
                    {
                        AppDialog.Show(Loc.T("Task Scheduler entry could not be created."), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    } else
                    {
                        AppDialog.Show(Loc.T("Added to task scheduler successfully. "), Loc.T("Success"), MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                } else
                {
                    AddWinlogonEntry(txtPath.Text);
                }

            
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                AppDialog.Show(Loc.F("Error:\n{0}", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog1 = new OpenFileDialog
            {
                InitialDirectory = @"C:\",
                Filter = Loc.T("All files (*.*)|*.*"),
                FilterIndex = 1,
                RestoreDirectory = true
            };

            bool? result = openFileDialog1.ShowDialog();

            if (result == true)
            {
                txtPath.Text = openFileDialog1.FileName;

                if (string.IsNullOrWhiteSpace(txtName.Text))
                {
                    txtName.Text = System.IO.Path.GetFileNameWithoutExtension(openFileDialog1.FileName);
                }
            }
        }

        private void rbWinLogon_Checked(object sender, RoutedEventArgs e)
        {
            txtName.IsEnabled = false;
            if (MethodHint != null)
                MethodHint.Text = Loc.T("Advanced: starts together with the Windows sign-in process. No name is needed.");
        }

        private void rbTaskScheduler_Checked(object sender, RoutedEventArgs e)
        {
            txtName.IsEnabled = true;
            if (MethodHint != null)
                MethodHint.Text = Loc.T("Starts through Task Scheduler when you sign in, with administrator rights.");
        }

        private void rbRegistry_Checked(object sender, RoutedEventArgs e)
        {
            txtName.IsEnabled = true;
            if (MethodHint != null)
                MethodHint.Text = Loc.T("Starts when you sign in to Windows. Recommended for most programs.");
        }
    }
}
