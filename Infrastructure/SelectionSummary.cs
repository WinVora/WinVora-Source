using System.Collections.Generic;
using System.Linq;

namespace WinVora
{
    internal readonly record struct SelectionState(int Total, int Selected)
    {
        public bool None => Selected == 0;
        public bool All => Total > 0 && Selected == Total;
        public bool Partial => Selected > 0 && Selected < Total;
    }

    internal static class SelectionSummary
    {
        public static SelectionState From(IEnumerable<bool> states)
        {
            var values = states.ToList();
            return new SelectionState(values.Count, values.Count(value => value));
        }
    }
}
