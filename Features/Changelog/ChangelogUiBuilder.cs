using System;

namespace WinVora
{
    internal static class ChangelogUiBuilder
    {
        public static string CategoryFor(string item, bool english)
        {
            string value = item.ToLowerInvariant();
            if (value.StartsWith("bugfix") || value.Contains("fixed") || value.Contains("behoben") || value.Contains("fehler"))
                return english ? "Bug fixes" : "Bugfixes";
            if (value.StartsWith("sicherheit") || value.StartsWith("safety") || value.Contains("warn") || value.Contains("neustart") || value.Contains("administrator"))
                return english ? "Safety" : "Sicherheit";
            if (value.StartsWith("oberfläche") || value.StartsWith("interface") || value.Contains("design") || value.Contains("sidebar") || value.Contains("fenster"))
                return english ? "Interface" : "Oberfläche";
            return english ? "Improvements" : "Verbesserungen";
        }

        public static string RemoveCategoryPrefix(string item)
        {
            int separator = item.IndexOf(':');
            return separator is > 0 and < 20 ? item[(separator + 1)..].Trim() : item;
        }
    }
}
