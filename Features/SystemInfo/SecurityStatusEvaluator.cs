using System;

namespace WinVora
{
    internal enum SecurityHealthState { Active, Unknown, Problem }

    internal static class SecurityStatusEvaluator
    {
        public static SecurityHealthState Evaluate(string antivirus, string firewall)
        {
            if (IsProblem(antivirus) || IsProblem(firewall)) return SecurityHealthState.Problem;
            if (IsUnknown(antivirus) || IsUnknown(firewall)) return SecurityHealthState.Unknown;
            return SecurityHealthState.Active;
        }

        private static bool IsProblem(string value) =>
            Contains(value, "Deaktiviert", "Disabled", "Inaktiv", "Inactive", "Teilweise", "Partial");

        private static bool IsUnknown(string value) => string.IsNullOrWhiteSpace(value) ||
            Contains(value,
                "Unbekannt", "Unknown",
                "Nicht verfügbar", "Not available",
                "Nicht prüfbar", "Not verifiable",
                "Konnte nicht geprüft werden", "Could not be checked");

        private static bool Contains(string value, params string[] terms)
            => Array.Exists(terms, term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
