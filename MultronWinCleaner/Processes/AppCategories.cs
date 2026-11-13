using System;

namespace MultronWinCleaner.Processes
{
    public enum AppCategory
    {
        Browsers,
        Communication,
        Games,
        Development,
        Office,
        Media,
        Security,
        Tools,
        Other
    }

    public static class AppCategories
    {
        public static readonly (AppCategory Category, string Glyph, string Title)[] Order =
        {
            (AppCategory.Browsers, "\uE774", "Browsers"),
            (AppCategory.Communication, "\uE8BD", "Internet & Communication"),
            (AppCategory.Media, "\uE8B9", "Media & Graphics"),
            (AppCategory.Office, "\uE8A5", "Office & Productivity"),
            (AppCategory.Development, "\uE943", "Development"),
            (AppCategory.Games, "\uE7FC", "Games"),
            (AppCategory.Security, "\uEA18", "Security"),
            (AppCategory.Tools, "\uE90F", "System Tools"),
            (AppCategory.Other, "\uE71D", "Other Applications")
        };

        private const string Prefix = "category:";

        public static AppCategory? FromHeader(string line)
        {
            foreach (string part in line.TrimStart('{').Split('='))
            {
                string value = part.Trim();
                if (value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
                    && Enum.TryParse(value.Substring(Prefix.Length).Trim(), true, out AppCategory category)
                    && Enum.IsDefined(category))
                    return category;
            }
            return null;
        }
    }
}
