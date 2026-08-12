using System;

namespace WinVora
{
    internal static class WingetTableParser
    {
        public static WingetPackage? Parse(string line, int[]? columns)
        {
            if (columns == null) return null;

            string Slice(int index)
            {
                if (index >= columns.Length) return "";
                int start = columns[index];
                int end = index + 1 < columns.Length ? columns[index + 1] : line.Length;
                if (start < 0 || start >= line.Length) return "";
                end = Math.Max(start, Math.Min(end, line.Length));
                return line.Substring(start, end - start).Trim();
            }

            var package = new WingetPackage
            {
                Name = Slice(0),
                Id = Slice(1),
                Version = Slice(2),
                Available = Slice(3),
                Source = Slice(4)
            };
            return string.IsNullOrWhiteSpace(package.Name) ? null : package;
        }
    }
}
