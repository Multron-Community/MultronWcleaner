using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace MultronWinCleaner.Processes
{
    public static class CsvExport
    {
        public static string Build(IEnumerable<string> header, IEnumerable<IEnumerable<string>> rows, string separator)
        {
            var text = new StringBuilder();
            text.AppendLine(string.Join(separator, header.Select(v => Quote(v, separator))));
            foreach (var row in rows)
                text.AppendLine(string.Join(separator, row.Select(v => Quote(v, separator))));
            return text.ToString();
        }

        public static void Write(string path, IEnumerable<string> header, IEnumerable<IEnumerable<string>> rows)
        {
            string separator = CultureInfo.CurrentCulture.TextInfo.ListSeparator;
            if (string.IsNullOrEmpty(separator))
                separator = ",";
            File.WriteAllText(path, Build(header, rows, separator), new UTF8Encoding(true));
        }

        public static void Save(Window owner, string defaultName, IEnumerable<string> header, IEnumerable<IEnumerable<string>> rows)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = defaultName + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm", CultureInfo.InvariantCulture) + ".csv",
                Filter = Loc.T("CSV file") + " (*.csv)|*.csv",
                DefaultExt = ".csv",
                AddExtension = true
            };
            if (dialog.ShowDialog(owner) != true)
                return;
            try
            {
                Write(dialog.FileName, header, rows);
                AppDialog.Show(owner, Loc.F("The list was saved to {0}", dialog.FileName), Loc.T("Export list"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppDialog.Show(owner, Loc.F("The list could not be saved: {0}", ex.Message), Loc.T("Export list"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string Quote(string? value, string separator)
        {
            value ??= "";
            if (value.Length > 0 && "=+-@".IndexOf(value[0]) >= 0)
                value = "'" + value;
            bool needsQuotes = value.Contains(separator) || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
            return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }
    }
}
