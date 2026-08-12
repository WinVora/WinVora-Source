using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WinVora
{
    internal static class ProgramListExporter
    {
        public static string ToText(IReadOnlyCollection<InstalledProgram> programs, bool english)
        {
            var lines = new List<string>
            {
                english ? "WinVora - Installed programs" : "WinVora - Installierte Programme",
                $"{(english ? "Created" : "Erstellt")}: {DateTime.Now:g}",
                $"{(english ? "Count" : "Anzahl")}: {programs.Count}",
                new string('-', 80)
            };
            lines.AddRange(programs.Select(program =>
                $"{program.DisplayName} | {Value(program.Version)} | {program.Publisher} | " +
                $"{Value(program.SizeDisplay, english ? "Unknown" : "Unbekannt")} | {Value(program.InstallDate)}"));
            return string.Join(Environment.NewLine, lines);
        }

        public static string ToCsv(IReadOnlyCollection<InstalledProgram> programs, bool english, char separator = ',')
        {
            string header = english
                ? "Name,Version,Publisher,Size,Install date"
                : "Name,Version,Herausgeber,Größe,Installationsdatum";
            header = header.Replace(',', separator);
            return header + Environment.NewLine + string.Join(Environment.NewLine, programs.Select(program =>
                string.Join(separator, new[] { program.DisplayName, program.Version, program.Publisher, program.SizeDisplay, program.InstallDate }.Select(Escape))));
        }

        private static string Value(string value, string fallback = "-") => string.IsNullOrWhiteSpace(value) ? fallback : value;
        private static string Escape(string value) => $"\"{(value ?? "").Replace("\"", "\"\"")}\"";
    }
}
