using System;

namespace WinVora
{
    internal enum SecurityHealthState { Active, Unknown, Problem }
    internal enum SecurityComponentState { Active, Partial, Disabled, Unknown }

    internal static class SecurityStatusEvaluator
    {
        public static SecurityHealthState Evaluate(string antivirus, string firewall)
        {
            return Evaluate(Parse(antivirus), Parse(firewall));
        }

        public static SecurityHealthState Evaluate(
            SecurityComponentState antivirus,
            SecurityComponentState firewall) =>
            antivirus is SecurityComponentState.Partial or SecurityComponentState.Disabled ||
            firewall is SecurityComponentState.Partial or SecurityComponentState.Disabled
                ? SecurityHealthState.Problem
                : antivirus == SecurityComponentState.Unknown || firewall == SecurityComponentState.Unknown
                    ? SecurityHealthState.Unknown
                    : SecurityHealthState.Active;

        public static string Format(SecurityComponentState state, bool english) => state switch
        {
            SecurityComponentState.Active => english ? "Active" : "Aktiv",
            SecurityComponentState.Partial => english ? "Partial/Inactive" : "Teilweise/Inaktiv",
            SecurityComponentState.Disabled => english ? "Disabled" : "Deaktiviert",
            _ => english ? "Unknown" : "Unbekannt"
        };

        private static SecurityComponentState Parse(string value)
        {
            if (IsProblem(value))
                return Contains(value, "Teilweise", "Partial", "Inaktiv", "Inactive")
                    ? SecurityComponentState.Partial
                    : SecurityComponentState.Disabled;
            return IsUnknown(value) ? SecurityComponentState.Unknown : SecurityComponentState.Active;
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
