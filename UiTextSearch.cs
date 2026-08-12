using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;

namespace WinVora
{
    internal static class UiTextSearch
    {
        public static string Collect(DependencyObject element)
        {
            var parts = new List<string>();

            void Visit(object? value)
            {
                switch (value)
                {
                    case null:
                        return;
                    case TextBlock text:
                        parts.Add(text.Text);
                        return;
                    case ComboBoxItem item:
                        Visit(item.Content);
                        return;
                    case ToggleSwitch toggle:
                        Visit(toggle.Header);
                        Visit(toggle.OnContent);
                        Visit(toggle.OffContent);
                        return;
                    case ContentControl contentControl:
                        Visit(contentControl.Content);
                        return;
                    case Panel panel:
                        foreach (var child in panel.Children) Visit(child);
                        return;
                    case Border border:
                        Visit(border.Tag);
                        Visit(border.Child);
                        return;
                    case string textValue:
                        parts.Add(textValue);
                        return;
                }
            }

            Visit(element);
            return string.Join(" ", parts);
        }
    }
}
