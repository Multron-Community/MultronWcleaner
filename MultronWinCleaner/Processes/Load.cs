using Multron_Win_Cleaner;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MultronWinCleaner;

namespace MultronWinCleaner.Processes
{
    public class Load
    {
        MainWindow main;
        CancellationTokenSource cts = new CancellationTokenSource();
        string? getline = null;

        private const string GlobeGlyph = "";
        private const string PersonGlyph = "";

        public static object SectionHeader(string title)
        {
            if (title.Length < 2 || title[0] < '' || title[0] > '' || title[1] != ' ')
                return title;
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new TextBlock
            {
                Text = title.Substring(0, 1),
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontWeight = FontWeights.Normal,
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 6, 0)
            });
            header.Children.Add(new TextBlock
            {
                Text = title.Substring(2),
                VerticalAlignment = VerticalAlignment.Center
            });
            return header;
        }

        public Load(MainWindow main)
        {
            this.main = main;
        }

        public async System.Threading.Tasks.Task ScandotsAsync(string text, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await main.Dispatcher.InvokeAsync(() => {
                        main.StatusLoad.Text = text + ".";
                        main.StatusLoad.Foreground = MultronWinCleaner.Processes.ThemeColors.ReadableBrush("#1e88e5");
                    });

                    await System.Threading.Tasks.Task.Delay(1000, cancellationToken);

                    await main.Dispatcher.InvokeAsync(() => {
                        main.StatusLoad.Text = text + "..";
                    });

                    await System.Threading.Tasks.Task.Delay(1000, cancellationToken);

                    await main.Dispatcher.InvokeAsync(() => {
                        main.StatusLoad.Text = text + "...";
                    });

                    await System.Threading.Tasks.Task.Delay(1000, cancellationToken);
                }
            }
            catch (TaskCanceledException)
            {
            }
        }

        private static bool PathExists(string path)
        {
            string checkPath = path.Contains('*') ? path.Substring(0, path.IndexOf('*')) : path;
            return Directory.Exists(checkPath) || File.Exists(checkPath);
        }

        private static List<string> GetProfileFolders(string root)
        {
            List<string> folders = Directory.GetDirectories(root).Select(f => new DirectoryInfo(f).Name).ToList();
            if (root.TrimEnd('\\').EndsWith("Profiles", StringComparison.OrdinalIgnoreCase)) return folders;

            List<string> profiles = folders.Where(name =>
                name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase) ||
                File.Exists(System.IO.Path.Combine(root, name, "Preferences")) ||
                File.Exists(System.IO.Path.Combine(root, name, "prefs.js"))).ToList();

            return profiles.Count > 0 ? profiles : folders;
        }

        private static int PickDefaultProfile(List<string> folders)
        {
            int index = folders.FindIndex(f => f.Equals("Default", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".default-release", StringComparison.OrdinalIgnoreCase) || f.EndsWith("(release)", StringComparison.OrdinalIgnoreCase));
            if (index < 0) index = folders.FindIndex(f => f.StartsWith("Profile", StringComparison.OrdinalIgnoreCase) || f.Contains("default", StringComparison.OrdinalIgnoreCase));
            return Math.Max(0, index);
        }

        public static IEnumerable<T> FindChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);

                if (child is T t)
                    yield return t;

                foreach (T descendant in FindChildren<T>(child))
                    yield return descendant;
            }
        }

        public static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T typedParent)
                    return typedParent;

                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        public string CreateRandomId(int length = 8)
        {
            const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

            Random rnd = new Random();
            char firstChar = letters[rnd.Next(letters.Length)];
            string rest = new string(Enumerable.Repeat(chars, length - 1)
                .Select(s => s[rnd.Next(s.Length)]).ToArray());

            return firstChar + rest;
        }

        private sealed class LoadEntry
        {
            public string Name = "";
            public string Path = "";
            public bool Recommended;
            public string? Warning;
            public bool IsProfile;
        }

        private sealed class ProfileLine
        {
            public string Name = "";
            public string SubPath = "";
            public bool Recommended;
            public string? Warning;
        }

        private sealed class LoadGroup
        {
            public string Header = "";
            public List<LoadEntry> Entries = new List<LoadEntry>();
            public string ProfileRoot = "";
            public List<string>? ProfileFolders;
            public int ProfileIndex;
            public List<ProfileLine> ProfileLines = new List<ProfileLine>();
            public List<System.Windows.Controls.CheckBox> ProfileCheckBoxes = new List<System.Windows.Controls.CheckBox>();
            public BrowserTag? Browser;
            public AppCategory? Category;
            public string? Source;
        }

        private readonly bool allBrowserProfiles = MainWindow.IsCleanAllBrowserProfilesEnabled();

        private sealed class LoadSection
        {
            public string? Title;
            public List<LoadGroup> Groups = new List<LoadGroup>();
        }

        private Dictionary<string, bool> selections = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, string> extraHeaderSources = new Dictionary<string, string>(StringComparer.Ordinal);

        public static string SelectionsPath => System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selections.txt");

        public static Dictionary<string, bool> ReadSelections()
        {
            var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(SelectionsPath))
                {
                    foreach (string line in File.ReadAllLines(SelectionsPath))
                    {
                        int separator = line.LastIndexOf('=');
                        if (separator > 0 && bool.TryParse(line.Substring(separator + 1), out bool value))
                        {
                            result[line.Substring(0, separator)] = value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Could not read selections.txt: " + ex.Message);
            }
            return result;
        }

        public static void WriteSelections(Dictionary<string, bool> selections)
        {
            string tempPath = SelectionsPath + ".tmp";
            File.WriteAllLines(tempPath, selections.Select(s => s.Key + "=" + (s.Value ? "true" : "false")));
            File.Move(tempPath, SelectionsPath, overwrite: true);
        }

        public async System.Threading.Tasks.Task RunAsync()
        {
            try
            {
                await main.Dispatcher.InvokeAsync(async () =>
                {
                    await main.loadothers();
                    await main.loadothers2();
                });

                string filePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "database.txt");
                if (System.IO.File.Exists(filePath))
                {
                    List<string> targetUsers = new List<string>();

                    await main.Dispatcher.InvokeAsync(() =>
                    {
                        targetUsers.Clear();

                        try
                        {
                            if (main.settings != null && main.settings.rbCleanAllUsers != null && main.settings.rbCleanAllUsers.IsChecked == true && main.settings.rbCleanSelectedUsers.IsChecked == false)
                            {
                                foreach (var item in main.settings.comboBoxUserSelection.Items)
                                {
                                    targetUsers.Add(item.ToString() ?? "");
                                }
                            }
                            else if (main.settings != null && main.settings.comboBoxUserSelection != null && main.settings.comboBoxUserSelection.SelectedItem != null)
                            {
                                targetUsers.Add(main.settings.comboBoxUserSelection.SelectedItem.ToString() ?? "");
                            }
                        }
                        catch (Exception)
                        {

                        }

                        if (targetUsers.Count == 0)
                        {
                            targetUsers.Add(Environment.UserName);
                        }
                    });

                    List<string> originalLines = System.IO.File.ReadAllLines(filePath).ToList();
                    var headerSources = new Dictionary<string, string>(StringComparer.Ordinal);
                    originalLines.AddRange(ExtraDatabases.ReadEnabledLines(headerSources));
                    extraHeaderSources = headerSources;
                    List<string> linesToProcess = BuildLinesToProcess(originalLines, targetUsers);
                    selections = ReadSelections();

                    await main.Dispatcher.InvokeAsync(() =>
                    {
                        var task = ScandotsAsync(Loc.T("Loading Database"), cts.Token);
                        main.buttonStartScan.IsEnabled = false;
                    });

                    List<LoadSection> sections = await System.Threading.Tasks.Task.Run(() => ResolveSections(linesToProcess));

                    await BuildSectionsAsync(sections, detailed: MainWindow.IsAdvancedInterface());

                    await main.Dispatcher.InvokeAsync(() =>
                    {
                        string? lastLine = null;
                        if (!string.IsNullOrWhiteSpace(main.settings.logfilepath) && File.Exists(main.settings.logfilepath))
                            lastLine = File.ReadLines(main.settings.logfilepath).LastOrDefault();

                        main.label1_Copy.Text = Loc.T("Ready to scan");
                        main.ShowLastLogLine(lastLine);

                        main.buttonStartScan.IsEnabled = true;
                        main.progressBar1.Value = 0;
                        main.UpdateArc(main.progressBar1.Value);
                        cts.Cancel();
                    });
                }
            }
            catch (Exception ex)
            {
                AppDialog.Show(ex.Message + " in Load.cs\n" + ex.StackTrace + "\n" + Loc.T("Line:") + " " + getline, Loc.T("database.txt error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<string> BuildLinesToProcess(List<string> originalLines, List<string> targetUsers)
        {
            List<string> globalStandalone = new List<string>();
            List<List<string>> globalBlocks = new List<List<string>>();
            Dictionary<string, List<string>> userStandalone = new Dictionary<string, List<string>>();
            Dictionary<string, List<List<string>>> userBlocks = new Dictionary<string, List<List<string>>>();

            foreach (var user in targetUsers)
            {
                userStandalone[user] = new List<string>();
                userBlocks[user] = new List<List<string>>();
            }

            bool inGroup = false;
            string blockHeader = "";
            List<string> currentBuffer = new List<string>();

            foreach (var line in originalLines)
            {
                string trim = line.Trim();
                if (string.IsNullOrWhiteSpace(trim)) continue;

                if (trim.StartsWith("{"))
                {
                    inGroup = true;
                    blockHeader = trim;
                    currentBuffer.Clear();
                }
                else if (trim.StartsWith("}"))
                {
                    inGroup = false;

                    List<string> globalBufferLines = new List<string>();
                    List<string> userBufferLines = new List<string>();

                    foreach (var l in currentBuffer)
                    {
                        bool isUserSpecific = l.Contains("{##}") ||
                                              l.Contains("AppData", StringComparison.OrdinalIgnoreCase) ||
                                              l.Contains("LocalSettings", StringComparison.OrdinalIgnoreCase) ||
                                              l.Contains("C:\\Users\\", StringComparison.OrdinalIgnoreCase) ||
                                              l.Contains("#profileget#", StringComparison.OrdinalIgnoreCase) ||
                                              l.Contains("#profile#", StringComparison.OrdinalIgnoreCase);

                        bool isProgramData = l.Contains("ProgramData", StringComparison.OrdinalIgnoreCase);

                        if (isUserSpecific && !isProgramData)
                        {
                            userBufferLines.Add(l);
                        }
                        else
                        {
                            globalBufferLines.Add(l);
                        }
                    }

                    if (globalBufferLines.Count > 0)
                    {
                        var gBlock = new List<string> { blockHeader };
                        gBlock.AddRange(globalBufferLines);
                        gBlock.Add("}");
                        globalBlocks.Add(gBlock);
                    }

                    if (userBufferLines.Count > 0)
                    {
                        foreach (var user in targetUsers)
                        {
                            var uBlock = new List<string> { blockHeader };
                            foreach (var l in userBufferLines)
                            {
                                uBlock.Add(ForUser(l, user));
                            }
                            uBlock.Add("}");
                            userBlocks[user].Add(uBlock);
                        }
                    }
                    currentBuffer.Clear();
                }
                else if (inGroup)
                {
                    currentBuffer.Add(trim);
                }
                else
                {
                    if (trim.Contains("{##}") || trim.Contains("AppData", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var user in targetUsers)
                        {
                            userStandalone[user].Add(ForUser(trim, user));
                        }
                    }
                    else
                    {
                        globalStandalone.Add(trim);
                    }
                }
            }

            List<string> linesToProcess = new List<string>();

            linesToProcess.Add("USER_START=" + GlobeGlyph + " " + Loc.T("System & Global Tools"));
            if (globalStandalone.Count > 0)
            {
                linesToProcess.Add("{=" + Loc.T("General Tools"));
                linesToProcess.AddRange(globalStandalone);
                linesToProcess.Add("}");
            }
            foreach (var block in globalBlocks) linesToProcess.AddRange(block);
            linesToProcess.Add("USER_END");

            foreach (var user in targetUsers)
            {
                bool hasContent = userBlocks[user].Count > 0 || userStandalone[user].Count > 0;
                if (hasContent)
                {
                    string userName = new DirectoryInfo(user).Name;
                    linesToProcess.Add($"USER_START={PersonGlyph} {userName}");

                    if (userStandalone[user].Count > 0)
                    {
                        linesToProcess.Add("{=" + Loc.T("User Specific Files"));
                        linesToProcess.AddRange(userStandalone[user]);
                        linesToProcess.Add("}");
                    }
                    foreach (var block in userBlocks[user]) linesToProcess.AddRange(block);

                    linesToProcess.Add("USER_END");
                }
            }

            return linesToProcess;
        }

        private static string ForUser(string line, string user)
        {
            if (line.Contains("{##}"))
            {
                return line.Replace("{##}", user);
            }

            string profileRoot = System.IO.Path.Combine("C:\\Users", user).Replace("$", "$$");
            return System.Text.RegularExpressions.Regex.Replace(line, @"C:\\Users\\[^\\]+", profileRoot, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private List<LoadSection> ResolveSections(List<string> lines)
        {
            var sections = new List<LoadSection>();
            var pending = new List<(LoadGroup Group, LoadEntry Entry, string CheckPath)>();
            main.browserPaths.Clear();
            main.warningPaths.Clear();
            main.extraDatabasePaths.Clear();

            LoadSection? section = null;
            LoadGroup? group = null;
            List<string>? profileTargets = null;

            foreach (string line in lines)
            {
                getline = line.Trim();

                if (line.StartsWith("USER_START="))
                {
                    section = new LoadSection { Title = line.Substring(11) };
                    sections.Add(section);
                    continue;
                }

                if (line == "USER_END")
                {
                    section = null;
                    continue;
                }

                if (line.StartsWith("{"))
                {
                    if (section == null)
                    {
                        section = new LoadSection();
                        sections.Add(section);
                    }
                    group = new LoadGroup
                    {
                        Header = main.stringtokenizer(line, "=", 1) ?? "",
                        Browser = BrowserTag.FromHeader(line),
                        Category = AppCategories.FromHeader(line),
                        Source = extraHeaderSources.TryGetValue(line.Trim(), out string? source) ? source : null
                    };
                    section.Groups.Add(group);
                    profileTargets = null;
                    continue;
                }

                if (line.StartsWith("}"))
                {
                    group = null;
                    profileTargets = null;
                    continue;
                }

                if (group == null) continue;

                try
                {
                    if (line.StartsWith("#profile#="))
                    {
                        string root = main.stringtokenizer(line, "=", 1) ?? "";
                        if (Directory.Exists(root))
                        {
                            List<string> folders = GetProfileFolders(root);
                            if (folders.Count > 0)
                            {
                                group.ProfileRoot = root;
                                group.ProfileFolders = folders;
                                if (group.Browser != null)
                                {
                                    group.Browser.ProfileRoot = root;
                                    group.Browser.ProfileFolders = folders;
                                }
                                group.ProfileIndex = PickDefaultProfile(folders);
                                profileTargets = allBrowserProfiles ? folders : new List<string> { folders[group.ProfileIndex] };
                            }
                        }
                        continue;
                    }

                    string? name = main.stringtokenizer(line, "=", 0);
                    string? path;
                    string? state;
                    string? warning = null;

                    if (line.Contains("#profileget#"))
                    {
                        if (profileTargets == null) continue;
                        string? subPath = main.stringtokenizer(line, "=", 2);
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(subPath) || !bool.TryParse(main.stringtokenizer(line, "=", 3), out bool profileRecommended))
                        {
                            Debug.WriteLine("Skipped invalid database line: " + line);
                            continue;
                        }

                        string? profileWarning = main.stringtokenizer(line, "=", 4);
                        if (string.IsNullOrWhiteSpace(profileWarning)) profileWarning = null;
                        group.ProfileLines.Add(new ProfileLine { Name = name, SubPath = subPath, Recommended = profileRecommended, Warning = profileWarning });
                        foreach (string folder in profileTargets)
                        {
                            string profilePath = group.ProfileRoot + folder + subPath;
                            pending.Add((group, new LoadEntry { Name = name, Path = profilePath, Recommended = profileRecommended, Warning = profileWarning, IsProfile = true }, profilePath));
                        }
                        continue;
                    }
                    else
                    {
                        path = main.stringtokenizer(line, "=", 1);
                        state = main.stringtokenizer(line, "=", 2);
                        warning = main.stringtokenizer(line, "=", 3);
                        if (warning == "true" || warning == "false") warning = null;
                    }

                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path) || !bool.TryParse(state, out bool recommended))
                    {
                        Debug.WriteLine("Skipped invalid database line: " + line);
                        continue;
                    }

                    pending.Add((group, new LoadEntry { Name = name, Path = path, Recommended = recommended, Warning = warning }, path));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Skipped database line: " + line + " (" + ex.Message + ")");
                }
            }

            bool[] exists = new bool[pending.Count];
            Parallel.For(0, pending.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) }, i =>
            {
                exists[i] = PathExists(pending[i].CheckPath);
            });

            var officialPaths = new HashSet<string>(
                pending.Where((p, i) => exists[i] && p.Group.Source == null).Select(p => p.Entry.Path),
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < pending.Count; i++)
            {
                if (!exists[i]) continue;
                pending[i].Group.Entries.Add(pending[i].Entry);
                if (pending[i].Group.Source is { } extraSource && !officialPaths.Contains(pending[i].Entry.Path))
                    main.extraDatabasePaths[pending[i].Entry.Path] = extraSource;
                if (pending[i].Entry.Warning is { } warning && !MainWindow.IsInfoNote(warning))
                    main.warningPaths[pending[i].Entry.Path] = warning;
                if (pending[i].Group.Browser is { } browser)
                {
                    main.browserPaths[pending[i].Entry.Path] = browser;
                    if (browser.ReferencePaths.Count < 5)
                        browser.ReferencePaths.Add(pending[i].Entry.Path);
                }
            }

            return sections;
        }

        private System.Windows.Controls.CheckBox CreateEntryCheckBox(LoadEntry entry, string groupId)
        {
            bool isChecked = selections.TryGetValue(entry.Path, out bool saved) ? saved : entry.Recommended;
            main.databaseDefaults[entry.Path] = entry.Recommended;
            if (isChecked) main.database.Add(entry.Name + "=" + entry.Path);

            System.Windows.Controls.CheckBox newCheckBox = new System.Windows.Controls.CheckBox
            {
                Name = groupId,
                Content = entry.Warning != null ? entry.Name + "=" + entry.Path + "=warning(" + entry.Warning + ")" : entry.Name + "=" + entry.Path,
                Margin = new Thickness(10),
                IsChecked = isChecked,
                BorderThickness = new Thickness(0),
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                Background = new SolidColorBrush(System.Windows.Media.Colors.White),
                Foreground = main.brush,
                FontSize = 12,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI")
            };
            newCheckBox.Checked += main.CheckBox_Checked;
            newCheckBox.Unchecked += main.CheckBox_Unchecked;
            newCheckBox.MouseEnter += (s, e) => main.ShowItemPaths(entry.Name, "", new[] { entry.Path });
            newCheckBox.GotKeyboardFocus += (s, e) => main.ShowItemPaths(entry.Name, "", new[] { entry.Path });
            main.checkboxes2.Add(newCheckBox);
            main.cleaningEntryBoxes.Add(newCheckBox);
            return newCheckBox;
        }

        #region Cleaning list

        private const string WindowsGlyph = "";

        private static readonly string[] WindowsGroupPrefixes =
        {
            "Windows", "Microsoft Store", "Internet Explorer", ".NET Framework", "IIS", "Xbox"
        };

        private static bool IsWindowsGroup(string header) =>
            WindowsGroupPrefixes.Any(p => header.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            || header == Loc.T("General Tools") || header == Loc.T("User Specific Files");

        public static string Prettify(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var sb = new StringBuilder(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c) && name[i - 1] != ' ')
                {
                    char prev = name[i - 1];
                    bool nextLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                    if (char.IsLower(prev) || char.IsDigit(prev) || (char.IsUpper(prev) && nextLower))
                        sb.Append(' ');
                }
                sb.Append(c == '_' ? ' ' : c);
            }
            return sb.ToString();
        }

        private sealed class SimpleRow
        {
            public string Name = "";
            public readonly List<System.Windows.Controls.CheckBox> Boxes = new List<System.Windows.Controls.CheckBox>();
            public System.Windows.Controls.CheckBox View = null!;
        }

        private sealed class SimpleGroup
        {
            public string Header = "";
            public string Title = "";
            public string GroupId = "";
            public readonly List<LoadGroup> Parts = new List<LoadGroup>();
            public readonly List<(System.Windows.Controls.CheckBox Box, LoadEntry Entry)> Items = new List<(System.Windows.Controls.CheckBox, LoadEntry)>();
            public readonly List<SimpleRow> Rows = new List<SimpleRow>();
            public StackPanel RowPanel = null!;
            public System.Windows.Controls.CheckBox HeaderBox = null!;
            public TextBlock CountText = null!;
            public bool Syncing;
            public bool Detailed;
            public Action? Refreshed;
        }

        private async System.Threading.Tasks.Task BuildSectionsAsync(List<LoadSection> sections, bool detailed)
        {
            var categories = new List<(string Title, List<SimpleGroup> Groups)>();
            if (detailed)
            {
                foreach (LoadSection section in sections)
                {
                    var groups = section.Groups.Select(g => new SimpleGroup { Header = g.Header, Detailed = true, Parts = { g } }).ToList();
                    categories.Add((section.Title ?? GlobeGlyph + " " + Loc.T("General Tools"), groups));
                }
            }
            else
            {
                var merged = new List<SimpleGroup>();
                var byHeader = new Dictionary<string, SimpleGroup>(StringComparer.OrdinalIgnoreCase);
                foreach (LoadGroup group in sections.SelectMany(s => s.Groups))
                {
                    if (!byHeader.TryGetValue(group.Header, out SimpleGroup? simple))
                    {
                        simple = new SimpleGroup { Header = group.Header };
                        byHeader[group.Header] = simple;
                        merged.Add(simple);
                    }
                    simple.Parts.Add(group);
                }

                categories.Add((WindowsGlyph + " " + Loc.T("Windows"), merged.Where(g => IsWindowsGroup(g.Header)).ToList()));
                var apps = merged.Where(g => !IsWindowsGroup(g.Header))
                    .OrderBy(g => Prettify(g.Header), StringComparer.CurrentCultureIgnoreCase)
                    .Select(g => (Group: g, Category: g.Parts.Any(p => p.Browser?.IsBrowser == true)
                        ? AppCategory.Browsers
                        : g.Parts.Select(p => p.Category).FirstOrDefault(c => c != null) ?? AppCategory.Other))
                    .ToList();
                foreach (var (category, glyph, title) in AppCategories.Order)
                    categories.Add((glyph + " " + Loc.T(title), apps.Where(a => a.Category == category).Select(a => a.Group).ToList()));
            }

            int total = Math.Max(1, categories.Sum(c => c.Groups.Count));
            int done = 0;
            foreach (var category in categories)
            {
                if (category.Groups.Count == 0) continue;

                System.Windows.Controls.Panel sectionPanel = null!;
                Expander sectionExpander = null!;
                bool cards = !detailed;
                await main.Dispatcher.InvokeAsync(() =>
                {
                    sectionPanel = cards ? CardLayout.CreateGrid(new Thickness(5)) : new StackPanel { Orientation = System.Windows.Controls.Orientation.Vertical, Margin = new Thickness(5) };
                    sectionExpander = new Expander
                    {
                        Header = SectionHeader(category.Title),
                        Tag = category.Title.Length > 2 ? category.Title.Substring(2) : category.Title,
                        Margin = new Thickness(5, 10, 5, 5),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(5, 5, 5, 10),
                        IsExpanded = true,
                        FontSize = 15,
                        FontWeight = FontWeights.Bold,
                        Content = sectionPanel
                    };
                    sectionExpander.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Text");
                    sectionExpander.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "BorderBrush");
                    main.SectionHost.Children.Add(sectionExpander);
                });

                foreach (SimpleGroup group in category.Groups)
                {
                    done++;
                    double progress = (double)done / total * 100;
                    await main.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            BuildSimpleGroup(group, sectionPanel, category.Title.Substring(0, 1));
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("Could not build group " + group.Header + ": " + ex.Message);
                        }
                        main.progressBar1.Value = progress;
                        main.SetLoadingProgress(progress);
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }

                await main.Dispatcher.InvokeAsync(() =>
                {
                    if (sectionPanel.Children.Count == 0)
                        (sectionExpander.Parent as System.Windows.Controls.Panel)?.Children.Remove(sectionExpander);
                });
            }

        }

        private void BuildSimpleGroup(SimpleGroup group, System.Windows.Controls.Panel sectionPanel, string categoryGlyph)
        {
            var profileParts = group.Parts.Where(p => !allBrowserProfiles && p.ProfileFolders != null && p.ProfileFolders.Count > 1).ToList();
            if (group.Parts.All(p => p.Entries.Count == 0) && profileParts.Count == 0) return;

            group.GroupId = CreateRandomId(8);
            foreach (LoadGroup part in group.Parts)
            {
                foreach (LoadEntry entry in part.Entries)
                {
                    var box = CreateEntryCheckBox(entry, group.GroupId);
                    if (entry.IsProfile) part.ProfileCheckBoxes.Add(box);
                    AddSimpleItem(group, box, entry);
                }
            }

            group.HeaderBox = new System.Windows.Controls.CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = Loc.T("Select or unselect the whole group")
            };
            group.HeaderBox.Click += (s, e) => SetGroupChecked(group, group.HeaderBox.IsChecked == true);

            bool card = sectionPanel is System.Windows.Controls.Grid;
            var title = new TextBlock { Text = group.Detailed ? group.Header : Prettify(group.Header), VerticalAlignment = VerticalAlignment.Center };
            group.CountText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Opacity = 0.6, FontWeight = FontWeights.Normal, FontSize = 12 };
            System.Windows.FrameworkElement header;
            (Border Pill, TextBlock Text) pill = default;
            if (card)
            {
                title.FontSize = 14;
                title.TextWrapping = TextWrapping.Wrap;
                if (title.Text.StartsWith("Windows ", StringComparison.OrdinalIgnoreCase) && title.Text.Substring(8).Trim().Contains(' '))
                    title.Text = title.Text.Substring(8).Trim();
                title.Margin = new Thickness(0, 0, 0, 2);
                group.Title = title.Text;
                var titleLine = new WrapPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                titleLine.Children.Add(title);
                var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                texts.Children.Add(titleLine);
                string? description = ItemInfo.Description(group.Header);
                if (description != null)
                {
                    var line = new TextBlock { Text = description, FontSize = 12, FontWeight = FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
                    line.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                    texts.Children.Add(line);
                }
                ItemInfo.Decorate(titleLine, title, group.Header, texts);
                pill = CardLayout.Pill();
                var grid = new System.Windows.Controls.Grid();
                grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
                string? infoKey = ItemInfo.Key(group.Header);
                var icon = CardLayout.Icon(infoKey != null || IsWindowsGroup(group.Header) ? ItemInfo.Glyph(infoKey) : categoryGlyph);
                System.Windows.Controls.Grid.SetColumn(texts, 1);
                System.Windows.Controls.Grid.SetColumn(pill.Pill, 2);
                grid.Children.Add(icon);
                grid.Children.Add(texts);
                grid.Children.Add(pill.Pill);
                header = grid;
            }
            else
            {
                var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                row.Children.Add(group.HeaderBox);
                row.Children.Add(title);
                row.Children.Add(group.CountText);
                ItemInfo.Decorate(row, title, group.Header);
                header = row;
            }

            group.RowPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Vertical, Margin = new Thickness(28, 2, 5, 6) };

            var expander = new Expander
            {
                Name = group.GroupId,
                Header = header,
                Tag = Prettify(group.Header) + " " + group.Header,
                Margin = new Thickness(5, 2, 5, 2),
                Background = new SolidColorBrush(Colors.Transparent),
                Foreground = main.brush,
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                Content = group.RowPanel
            };
            main.expanders.Add(expander);

            foreach (LoadGroup part in profileParts)
                group.RowPanel.Children.Add(CreateSimpleProfileList(group, part));

            if (card)
            {
                group.RowPanel.Margin = new Thickness(60, 0, 12, 10);
                CardLayout.MakeSelectable(expander, header, () =>
                {
                    bool anyOn = group.Items.Any(i => i.Box.IsChecked == true);
                    if (anyOn)
                        SetGroupChecked(group, false);
                    else
                    {
                        var recommended = group.Items.Where(i => i.Entry.Recommended).Select(i => i.Box).ToList();
                        SetBoxes(group, recommended.Count > 0 ? recommended : group.Items.Select(i => i.Box), true);
                    }
                });
                group.Refreshed = () =>
                {
                    int selected = group.Items.Count(i => i.Box.IsChecked == true);
                    CardLayout.SetSelected(expander, selected > 0);
                    CardLayout.SetPill(pill.Pill!, pill.Text!, selected > 0, Loc.F("{0} / {1} selected", selected, group.Items.Count));
                };
            }

            RebuildSimpleRows(group);
            if (sectionPanel is System.Windows.Controls.Grid cardGrid)
                CardLayout.Add(cardGrid, expander);
            else
                sectionPanel.Children.Add(expander);
            group.Refreshed?.Invoke();
        }

        private void AddSimpleItem(SimpleGroup group, System.Windows.Controls.CheckBox box, LoadEntry entry)
        {
            group.Items.Add((box, entry));
            box.Checked += (s, e) => OnSimpleItemChanged(group);
            box.Unchecked += (s, e) => OnSimpleItemChanged(group);
        }

        private void OnSimpleItemChanged(SimpleGroup group)
        {
            if (!group.Syncing)
                RefreshSimpleGroup(group);
        }

        private void SetGroupChecked(SimpleGroup group, bool isChecked)
        {
            SetBoxes(group, group.Items.Select(i => i.Box), isChecked);
        }

        private void SetBoxes(SimpleGroup group, IEnumerable<System.Windows.Controls.CheckBox> boxes, bool isChecked)
        {
            group.Syncing = true;
            try
            {
                foreach (var box in boxes.ToList())
                    if (box.IsChecked != isChecked)
                        box.IsChecked = isChecked;
            }
            finally
            {
                group.Syncing = false;
            }
            RefreshSimpleGroup(group);
        }

        private static bool? CombinedState(IEnumerable<System.Windows.Controls.CheckBox> boxes)
        {
            bool any = false, all = true, none = true;
            foreach (var box in boxes)
            {
                any = true;
                if (box.IsChecked == true) none = false; else all = false;
            }
            if (!any) return false;
            return all ? true : none ? false : null;
        }

        private void RefreshSimpleGroup(SimpleGroup group)
        {
            foreach (SimpleRow row in group.Rows)
                row.View.IsChecked = CombinedState(row.Boxes);
            if (group.HeaderBox != null)
                group.HeaderBox.IsChecked = CombinedState(group.Items.Select(i => i.Box));
            if (group.CountText != null)
            {
                int selected = group.Rows.Count(r => r.View.IsChecked != false);
                group.CountText.Text = Loc.F("{0} / {1} selected", selected, group.Rows.Count);
            }
            group.Refreshed?.Invoke();
        }

        private void RebuildSimpleRows(SimpleGroup group)
        {
            foreach (SimpleRow old in group.Rows)
                group.RowPanel.Children.Remove(old.View);
            group.Rows.Clear();

            var byName = new Dictionary<string, SimpleRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var (box, entry) in group.Items)
            {
                string key = group.Detailed ? entry.Path : entry.Name;
                if (!byName.TryGetValue(key, out SimpleRow? row))
                {
                    row = new SimpleRow { Name = entry.Name };
                    byName[key] = row;
                    group.Rows.Add(row);
                }
                row.Boxes.Add(box);
            }

            foreach (SimpleRow row in group.Rows)
            {
                List<string> paths = row.Boxes.Select(b => main.stringtokenizer(b.Content?.ToString() ?? "", "=", 1) ?? "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                string? tip = null;
                string? note = MainWindow.ShowEntryNotes()
                    ? group.Items.Where(i => row.Boxes.Contains(i.Box)).Select(i => i.Entry.Warning).FirstOrDefault(w => !string.IsNullOrEmpty(w))
                    : null;
                bool info = note != null && MainWindow.IsInfoNote(note);
                string? warning = note == null ? null : Loc.T(note);
                if (warning != null)
                    tip = info ? warning : Loc.F("Warning: {0}", warning);

                object content = warning != null ? WithWarningButton(Prettify(row.Name), warning, info) : new TextBlock { Text = Prettify(row.Name), TextWrapping = TextWrapping.Wrap };
                if (group.Detailed)
                {
                    var lines = new StackPanel();
                    lines.Children.Add(warning != null ? WithWarningButton(row.Name, warning, info) : new TextBlock { Text = row.Name, TextWrapping = TextWrapping.Wrap });
                    var pathText = new TextBlock { Text = string.Join("\n", paths), FontSize = 11, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) };
                    lines.Children.Add(pathText);
                    content = lines;
                }

                var view = new System.Windows.Controls.CheckBox
                {
                    Content = content,
                    Tag = Prettify(row.Name) + " " + row.Name + " " + string.Join(" ", paths),
                    ToolTip = tip,
                    Margin = new Thickness(0, 3, 0, 3),
                    FontSize = 12.5,
                    FontWeight = FontWeights.Normal,
                    FontFamily = new FontFamily("Segoe UI")
                };
                view.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Text");
                SimpleRow current = row;
                string groupTitle = group.Title.Length > 0 ? group.Title : group.Detailed ? group.Header : Prettify(group.Header);
                string rowTitle = group.Detailed ? row.Name : Prettify(row.Name);
                view.MouseEnter += (s, e) => main.ShowItemPaths(rowTitle, groupTitle, paths);
                view.GotKeyboardFocus += (s, e) => main.ShowItemPaths(rowTitle, groupTitle, paths);
                view.Click += (s, e) => SetBoxes(group, current.Boxes, view.IsChecked == true);
                view.ContextMenu = CreateSimpleRowMenu(paths);
                row.View = view;
                group.RowPanel.Children.Add(view);
            }

            RefreshSimpleGroup(group);
        }

        private static WrapPanel WithWarningButton(string text, string warning, bool info)
        {
            var orange = (Color)ColorConverter.ConvertFromString(info ? "#1E88E5" : "#FF9800");
            var button = new Button
            {
                Content = info ? "ⓘ " + Loc.T("Info") : "⚠ " + Loc.T("Warning"),
                FontSize = 11,
                Padding = new Thickness(6, 0, 6, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = new SolidColorBrush(Color.FromArgb(0x26, orange.R, orange.G, orange.B)),
                BorderBrush = new SolidColorBrush(orange),
                BorderThickness = new Thickness(1),
                Foreground = ThemeColors.ReadableBrush(orange),
                ToolTip = warning
            };
            button.Click += (s, e) =>
            {
                e.Handled = true;
                AppDialog.Show(warning, text, MessageBoxButton.OK, info ? MessageBoxImage.Information : MessageBoxImage.Warning);
            };
            var panel = new WrapPanel();
            panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(button);
            return panel;
        }

        private System.Windows.Controls.ComboBox CreateSimpleProfileList(SimpleGroup group, LoadGroup part)
        {
            var list = new System.Windows.Controls.ComboBox
            {
                FontSize = 12.5,
                Margin = new Thickness(0, 2, 0, 6),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                MinWidth = 220,
                ToolTip = Loc.T("Browser profile to clean") + "\n" + part.ProfileRoot
            };
            foreach (string folder in part.ProfileFolders!)
                list.Items.Add(folder);
            list.SelectedIndex = part.ProfileIndex;
            list.SelectionChanged += (s, e) =>
            {
                if (list.SelectedItem is not string folder) return;
                try
                {
                    foreach (var oldBox in part.ProfileCheckBoxes)
                    {
                        string content = oldBox.Content?.ToString() ?? "";
                        main.database.Remove(main.stringtokenizer(content, "=", 0) + "=" + main.stringtokenizer(content, "=", 1));
                        main.checkboxes2.Remove(oldBox);
                        main.cleaningEntryBoxes.Remove(oldBox);
                        group.Items.RemoveAll(i => i.Box == oldBox);
                    }
                    part.ProfileCheckBoxes.Clear();

                    foreach (ProfileLine line in part.ProfileLines)
                    {
                        string path = part.ProfileRoot + folder + line.SubPath;
                        if (!PathExists(path)) continue;
                        var entry = new LoadEntry { Name = line.Name, Path = path, Recommended = line.Recommended, Warning = line.Warning, IsProfile = true };
                        if (entry.Warning != null && !MainWindow.IsInfoNote(entry.Warning))
                            main.warningPaths[path] = entry.Warning;
                        if (part.Browser != null)
                            main.browserPaths[path] = part.Browser;
                        if (part.Source != null)
                            main.extraDatabasePaths[path] = part.Source;
                        var box = CreateEntryCheckBox(entry, group.GroupId);
                        part.ProfileCheckBoxes.Add(box);
                        AddSimpleItem(group, box, entry);
                    }
                    RebuildSimpleRows(group);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Could not switch browser profile in " + group.Header + ": " + ex.Message);
                }
            };
            list.Height = 32;
            MainWindow.StyleCardComboBox(list);
            main.comboboxlist.Add(list);
            return list;
        }

        private static ContextMenu CreateSimpleRowMenu(List<string> paths)
        {
            var menu = new ContextMenu();
            var open = new MenuItem { Header = Loc.T("Open location") };
            open.Click += (s, e) =>
            {
                string? target = paths.Select(p => Environment.ExpandEnvironmentVariables(p.Contains('*') ? p.Substring(0, p.IndexOf('*')) : p))
                    .FirstOrDefault(p => Directory.Exists(p) || File.Exists(p));
                if (target == null)
                {
                    AppDialog.Show(Loc.F("Path not found:\n{0}", paths.FirstOrDefault() ?? ""), Loc.T("Open File Location"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                try
                {
                    if (File.Exists(target))
                        Process.Start("explorer.exe", $"/select,\"{target}\"");
                    else
                        Process.Start("explorer.exe", $"\"{target}\"");
                }
                catch (Exception ex)
                {
                    AppDialog.Show(Loc.F("Error:\n{0}", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            var copy = new MenuItem { Header = Loc.T("Copy path") };
            copy.Click += (s, e) =>
            {
                try { System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, paths)); }
                catch (Exception ex) { AppDialog.Show(Loc.F("Error:\n{0}", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error); }
            };
            menu.Items.Add(open);
            menu.Items.Add(copy);
            return menu;
        }

        #endregion

    }
}
