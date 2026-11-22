using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Markup;

namespace MultronWinCleaner
{
    public static class Loc
    {
        public const string SettingKey = "language";
        public const string DefaultLanguage = "en";
        public const string AutoLanguage = "auto";

        public static readonly (string Code, string Name)[] Languages =
        {
            ("en", "English"),
            ("tr", "Türkçe"),
            ("de", "Deutsch"),
            ("fr", "Français"),
            ("es", "Español"),
            ("it", "Italiano"),
            ("pl", "Polski"),
            ("pt-br", "Português (Brasil)"),
            ("ru", "Русский"),
            ("ja", "日本語"),
            ("zh-cn", "简体中文"),
            ("zh-tw", "繁體中文"),
            ("ko", "한국어"),
        };

        public static string DetectWindowsLanguage()
        {
            try
            {
                CultureInfo culture = CultureInfo.CurrentUICulture;
                string name = culture.Name.ToLowerInvariant();
                if (name.StartsWith("zh"))
                {
                    bool traditional = name.Contains("hant") || name.EndsWith("-tw") || name.EndsWith("-hk") || name.EndsWith("-mo");
                    return traditional ? "zh-tw" : "zh-cn";
                }
                if (name.StartsWith("pt"))
                    return "pt-br";
                string twoLetter = culture.TwoLetterISOLanguageName.ToLowerInvariant();
                if (Languages.Any(l => l.Code == twoLetter))
                    return twoLetter;
            }
            catch { }
            return DefaultLanguage;
        }

        public static string Resolve(string code) => code == AutoLanguage ? DetectWindowsLanguage() : code;

        private static Dictionary<string, string> strings = new Dictionary<string, string>(StringComparer.Ordinal);
        private static Dictionary<string, string> reverse = new Dictionary<string, string>(StringComparer.Ordinal);

        public static string Language { get; private set; } = DefaultLanguage;

        public static bool IsEnglish => Language == DefaultLanguage;

        public static CultureInfo Culture => IsEnglish ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(Language);

        private static string SettingsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.txt");

        public static bool IsAutomatic { get; private set; }

        public static string LanguageName => Languages.FirstOrDefault(l => l.Code == Language).Name ?? "English";

        public static string StartupLanguageText => IsAutomatic
            ? F("Language: {0} (automatic, from Windows)", LanguageName)
            : F("Language: {0}", LanguageName);

        public static void Init()
        {
            string saved = ReadSavedLanguage();
            IsAutomatic = saved == AutoLanguage;
            Load(Resolve(saved));
        }

        public static string ReadSavedLanguage()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    foreach (string line in File.ReadAllLines(SettingsPath))
                    {
                        if (line.StartsWith(SettingKey + ":", StringComparison.OrdinalIgnoreCase))
                        {
                            string code = line.Substring(SettingKey.Length + 1).Trim().ToLowerInvariant();
                            if (code == AutoLanguage || Languages.Any(l => l.Code == code))
                                return code;
                        }
                    }
                }
            }
            catch { }
            return AutoLanguage;
        }

        public static void SaveLanguage(string code)
        {
            var lines = File.Exists(SettingsPath) ? File.ReadAllLines(SettingsPath).ToList() : new List<string>();
            string entry = $"{SettingKey}:{code}";
            int index = lines.FindIndex(l => l.StartsWith(SettingKey + ":", StringComparison.OrdinalIgnoreCase));
            if (index != -1)
                lines[index] = entry;
            else
                lines.Add(entry);
            File.WriteAllLines(SettingsPath, lines);
        }

        public static void Load(string code)
        {
            strings = new Dictionary<string, string>(StringComparer.Ordinal);
            reverse = new Dictionary<string, string>(StringComparer.Ordinal);
            Language = DefaultLanguage;
            if (code == DefaultLanguage) return;

            try
            {
                using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"MultronWinCleaner.Lang.{code}.json");
                if (stream == null) return;
                var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
                if (loaded == null) return;

                foreach (var pair in loaded)
                {
                    if (string.IsNullOrEmpty(pair.Value)) continue;
                    strings[pair.Key] = pair.Value;
                    reverse.TryAdd(pair.Value, pair.Key);
                }
                Language = code;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Language file could not be loaded: " + ex.Message);
            }
        }

        public static string T(string text)
        {
            if (string.IsNullOrEmpty(text) || strings.Count == 0) return text;
            if (strings.TryGetValue(text, out string? translated)) return translated;
#if DEBUG
            Missing(text);
#endif
            return text;
        }

        public static string T(string text, string context)
        {
            if (strings.TryGetValue(context + "|" + text, out string? translated)) return translated;
            return T(text);
        }

        public static string F(string format, params object?[] args)
        {
            return string.Format(T(format), args);
        }

        public static string En(object? text)
        {
            string value = text?.ToString() ?? "";
            return reverse.TryGetValue(value, out string? english) ? english : value;
        }

#if DEBUG
        private static readonly bool logMissing = Environment.GetEnvironmentVariable("MWC_LOG_MISSING_TRANSLATIONS") == "1";
        private static readonly HashSet<string> missing = new HashSet<string>(StringComparer.Ordinal);

        private static void Missing(string text)
        {
            if (!logMissing) return;
            lock (missing)
            {
                if (!missing.Add(text)) return;
                try
                {
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"missing_{Language}.txt"),
                        text.Replace("\r", "\\r").Replace("\n", "\\n") + Environment.NewLine);
                }
                catch { }
            }
        }
#endif

        public static void Restart()
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory });
            }
            Environment.Exit(0);
        }
    }

    [MarkupExtensionReturnType(typeof(string))]
    public class TrExtension : MarkupExtension
    {
        public TrExtension() { }

        public TrExtension(string text)
        {
            Text = text;
        }

        [ConstructorArgument("text")]
        public string Text { get; set; } = "";

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return Loc.T(Text);
        }
    }
}
