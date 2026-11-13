using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MultronWinCleaner.Processes
{
    public static class ItemInfo
    {
        public enum Badge { None, Advanced, Slow, Testing }

        private static readonly Dictionary<string, (string Description, Badge Badge)> Items = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Windows Temporary Files"] = ("Files that Windows and programs made for short-term use. Safe to delete.", Badge.None),
            ["Windows Recycle Bin"] = ("Files you deleted. Emptying the Recycle Bin removes them for good.", Badge.None),
            ["Windows Update"] = ("Downloaded update files and update logs. Windows downloads them again when needed.", Badge.None),
            ["Windows Delivery Optimization"] = ("Update pieces Windows keeps to share with other PCs. Safe to delete.", Badge.None),
            ["Windows Upgrade Leftovers"] = ("Files kept after a Windows upgrade. Deleting them frees a lot of space, but you can no longer go back to the previous Windows version.", Badge.Advanced),
            ["Windows Component Store & Servicing"] = ("Old versions of Windows components. Cleaning frees space, but some installed updates can no longer be uninstalled.", Badge.Advanced),
            ["Windows Installer & Package Caches"] = ("Copies of program installers. Some programs need them to repair or uninstall themselves.", Badge.Advanced),
            ["Windows Error Reporting & Crash Dumps"] = ("Error reports and crash memory dumps. Only needed when looking into crashes.", Badge.None),
            ["Windows Logs"] = ("Log files Windows writes for troubleshooting. Safe to delete.", Badge.None),
            ["Windows Setup Logs"] = ("Logs from installing Windows and updates. Only needed to look into setup problems.", Badge.None),
            ["Windows Diagnostics & Reports"] = ("Diagnostic data and reports collected by Windows.", Badge.None),
            ["Windows Thumbnail & Icon Cache"] = ("Picture previews and icons. Windows makes them again; folders may open a little slower at first.", Badge.None),
            ["Windows Font Cache"] = ("Font data that Windows rebuilds by itself.", Badge.None),
            ["Windows System Caches"] = ("Caches of Windows components. They are rebuilt when needed.", Badge.None),
            ["Windows User Caches"] = ("Caches of your user account. They are rebuilt when needed.", Badge.None),
            ["Windows Service Account Caches"] = ("Caches of the accounts Windows background services use.", Badge.Advanced),
            ["Windows Search"] = ("The search index. Windows rebuilds it; searching may be slower until it finishes.", Badge.None),
            ["Windows Personal Data"] = ("Recent file lists, history and other traces of what you did on this PC.", Badge.Advanced),
            ["Windows User Folders"] = ("Files in your own folders, such as Downloads. Check what is selected before cleaning.", Badge.Advanced),
            ["Windows System Files (Advanced)"] = ("System files that only experienced users should remove.", Badge.Advanced),
            ["Windows Enterprise Management"] = ("Data used when a company manages the PC. Do not clean it on a work PC.", Badge.Advanced),
            ["Windows Disk Cleanup"] = ("Runs the Windows Disk Cleanup tool with the selected categories.", Badge.None),
            ["Windows Repair (DISM)"] = ("Checks and repairs the store Windows installs its components from. Takes several minutes.", Badge.Slow),
            ["System File Check (SFC)"] = ("Checks the Windows system files and repairs damaged ones. Takes several minutes.", Badge.Slow),
            ["Deep Log Files Scan"] = ("Searches the whole drive for log files. Takes longer than a normal scan.", Badge.Slow),
            ["Malware Scan"] = ("Checks the files on your PC for malware during the scan.", Badge.Testing),
            ["Duplicate Files"] = ("Finds identical copies of files so you can remove the extra ones.", Badge.None),
            ["Large Files"] = ("Finds big files you have not used for a long time, so you can decide what to remove.", Badge.None),
            ["Firewall Rules"] = ("Finds firewall rules for programs that are no longer installed.", Badge.None),
            ["Shortcuts"] = ("Finds broken shortcuts that point to missing files.", Badge.None),
            ["Security"] = ("Checks the Windows security settings and shows what should be fixed.", Badge.None),
        };

        private static bool TryFind(string? name, out (string Description, Badge Badge) info)
        {
            info = default;
            string? key = Key(name);
            return key != null && Items.TryGetValue(key, out info);
        }

        private static readonly Dictionary<string, string> AdvancedTitles = new(StringComparer.OrdinalIgnoreCase)
        {
            ["cleanmgr.exe Commands"] = "Windows Disk Cleanup",
            ["Dism.exe Commands"] = "Windows Repair (DISM)",
            ["SFC Commands"] = "System File Check (SFC)",
        };

        public static string? Description(string? name) => TryFind(name, out var info) ? Loc.T(info.Description) : null;

        private static readonly Dictionary<string, string> Glyphs = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Windows Disk Cleanup"] = "\uE74D",
            ["Windows Repair (DISM)"] = "\uEC7A",
            ["System File Check (SFC)"] = "\uE8A5",
            ["Deep Log Files Scan"] = "\uE721",
            ["Malware Scan"] = "\uEA18",
            ["Duplicate Files"] = "\uE8C8",
            ["Firewall Rules"] = "\uE774",
            ["Shortcuts"] = "\uE71B",
            ["Security"] = "\uE72E",
            ["Windows Temporary Files"] = "\uE823",
            ["Windows Recycle Bin"] = "\uE74D",
            ["Windows Update"] = "\uE895",
            ["Windows Delivery Optimization"] = "\uE896",
            ["Windows Upgrade Leftovers"] = "\uE898",
            ["Windows Component Store & Servicing"] = "\uE8F1",
            ["Windows Installer & Package Caches"] = "\uE8E5",
            ["Windows Error Reporting & Crash Dumps"] = "\uE7BA",
            ["Windows Logs"] = "\uE7C3",
            ["Windows Setup Logs"] = "\uE9F9",
            ["Windows Diagnostics & Reports"] = "\uE9D9",
            ["Windows Thumbnail & Icon Cache"] = "\uE8B9",
            ["Windows Font Cache"] = "\uE8D2",
            ["Windows System Caches"] = "\uE950",
            ["Windows User Caches"] = "\uE8D4",
            ["Windows Service Account Caches"] = "\uE912",
            ["Windows Search"] = "\uE7C5",
            ["Windows Widgets"] = "\uECAA",
            ["Windows Shell Apps"] = "\uE737",
            ["Windows Security & Sign-in Apps"] = "\uE8D7",
            ["Microsoft Store & WinGet"] = "\uE719",
            ["Internet Explorer & Legacy Edge"] = "\uE774",
            [".NET Framework & PowerShell"] = "\uE943",
            ["IIS Web Server"] = "\uE968",
            ["Windows Remote Desktop"] = "\uE8AF",
            ["Windows Printing"] = "\uE749",
            ["Xbox Services"] = "\uE7FC",
            ["Windows AI & Copilot"] = "\uE99A",
            ["Windows Enterprise Management"] = "\uE821",
            ["Windows Administrator Protection"] = "\uE7EF",
            ["Windows Personal Data"] = "\uE77B",
            ["Windows User Folders"] = "\uE8B7",
            ["Windows System Files (Advanced)"] = "\uE835",
            ["Microsoft Store Default Apps"] = "\uE71D",
            ["Windows 8.1 & 10 Legacy Apps"] = "\uE8FC",
            ["Windows Notepad"] = "\uE70B",
            ["Xbox App"] = "\uE990",
            ["Xbox Game Bar"] = "\uE8B2",
            ["Windows Terminal"] = "\uE756",
            ["Xbox PC App"] = "\uE7F8",
            ["Windows Subsystem for Linux"] = "\uE81E",
        };

        public static string? Key(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            string trimmed = name.Trim();
            foreach (var title in AdvancedTitles)
            {
                if (string.Equals(title.Key, trimmed, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Loc.T(title.Key), trimmed, StringComparison.CurrentCultureIgnoreCase))
                    return title.Value;
            }
            foreach (string key in Items.Keys)
            {
                if (string.Equals(key, trimmed, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Loc.T(key), trimmed, StringComparison.CurrentCultureIgnoreCase)
                    || trimmed.StartsWith(Loc.T(key) + " (", StringComparison.CurrentCultureIgnoreCase))
                    return key;
            }
            return null;
        }

        public static string Glyph(string? key) => key != null && Glyphs.TryGetValue(key, out string? glyph) ? glyph : "\uE8B7";

        public static void Decorate(Panel header, TextBlock title, string? name, Panel? slowBadgeHost = null)
        {
            if (!TryFind(name, out var info))
                return;
            title.ToolTip = Loc.T(info.Description);
            ToolTipService.SetInitialShowDelay(title, 300);
            if (info.Badge == Badge.None)
                return;
            if (info.Badge == Badge.Testing && name!.Contains("(" + Loc.T("Testing") + ")", StringComparison.CurrentCultureIgnoreCase))
                return;
            var color = info.Badge == Badge.Slow ? Color.FromRgb(0x42, 0x9F, 0xF5) : Color.FromRgb(0xFB, 0x8C, 0x00);
            string badgeText = info.Badge == Badge.Advanced ? "Advanced" : info.Badge == Badge.Slow ? "Takes a while" : "Testing";
            string badgeTip = info.Badge == Badge.Advanced ? "Check what this removes before cleaning."
                : info.Badge == Badge.Slow ? "This step can take several minutes."
                : "The cloud malware scan is published for testing so it can be tried thoroughly and made stable. Results and behavior may still change.";
            var badge = new Border
            {
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(6, 1, 6, 1),
                CornerRadius = new CornerRadius(6),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(0x26, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, color.R, color.G, color.B)),
                BorderThickness = new Thickness(1),
                ToolTip = Loc.T(badgeTip),
                Child = new TextBlock
                {
                    Text = Loc.T(badgeText),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = ThemeColors.ReadableBrush(color)
                }
            };
            if (info.Badge != Badge.Slow)
            {
                badge.SetResourceReference(Border.BackgroundProperty, "BadgeWarningBackground");
                badge.SetResourceReference(Border.BorderBrushProperty, "BadgeWarningBorder");
                ((TextBlock)badge.Child).SetResourceReference(TextBlock.ForegroundProperty, "BadgeWarningText");
            }
            else
            {
                ((TextBlock)badge.Child).SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            }
            if ((info.Badge == Badge.Slow || info.Badge == Badge.Testing) && slowBadgeHost != null)
            {
                badge.Margin = new Thickness(0, 7, 0, 0);
                badge.HorizontalAlignment = HorizontalAlignment.Left;
                slowBadgeHost.Children.Add(badge);
                return;
            }
            int index = header.Children.IndexOf(title);
            header.Children.Insert(index < 0 ? header.Children.Count : index + 1, badge);
        }
    }
}
