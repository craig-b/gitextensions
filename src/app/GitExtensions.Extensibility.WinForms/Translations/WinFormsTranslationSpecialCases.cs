using System.Collections;
using System.Reflection;
using GitExtensions.Extensibility.Translations;
using GitExtensions.Extensibility.Translations.Xliff;

namespace GitExtensions.Extensibility.WinForms.Translations;

/// <summary>
///  The WinForms special cases of the translation walk, moved verbatim out of
///  <see cref="TranslationUtil"/> in M7 so that class (and with it the whole
///  Extensibility/plugin surface) is platform-neutral: ToolTip title/pairing entries,
///  DataGridViewColumn.HeaderText, and ComboBox/ListBox item lists. Installed via
///  <see cref="Install"/> by every WinForms host before the first translation walk
///  (GitExtensionsControlInitialiser's static constructor covers the app; TranslationApp and
///  the translation round-trip test install explicitly).
/// </summary>
public sealed class WinFormsTranslationSpecialCases : ITranslationSpecialItemHandler
{
    public static void Install() => TranslationUtil.SpecialItemHandler ??= new WinFormsTranslationSpecialCases();

    public bool TryAddItems(string category, ITranslation translation, string itemName, object itemObj, IEnumerable<(string Name, object Item)> allItems)
    {
        if (itemObj is not ToolTip tooltip)
        {
            return false;
        }

        string toolTipTitle = tooltip.ToolTipTitle;

        if (!string.IsNullOrEmpty(toolTipTitle))
        {
            translation.AddTranslationItem(category, itemName, "ToolTipTitle", toolTipTitle);
        }

        foreach ((string itemNameForTooltip, object itemObjForTooltip) in allItems)
        {
            if (itemObjForTooltip is Control control)
            {
                string? tooltipString = tooltip.GetToolTip(control);
                if (!string.IsNullOrEmpty(tooltipString))
                {
                    // Will add an entry in the xlf file with id `NameOfControl.NameOfTooltipControl` ex: "PushToRemote.toolTip1"
                    translation.AddTranslationItem(category, itemNameForTooltip, itemName, tooltipString);
                }
            }
        }

        return true;
    }

    public bool TryTranslateItems(string category, ITranslation translation, string itemName, object itemObj, IEnumerable<(string Name, object Item)> allItems)
    {
        if (itemObj is not ToolTip tooltip)
        {
            return false;
        }

        static string? ProvideDefaultValue() => null;

        string? toolTipTitle = translation.TranslateItem(category, itemName, "ToolTipTitle", ProvideDefaultValue);

        if (!string.IsNullOrEmpty(toolTipTitle))
        {
            tooltip.ToolTipTitle = toolTipTitle;
        }

        foreach ((string itemNameForTooltip, object itemObjForTooltip) in allItems)
        {
            if (itemObjForTooltip is Control control)
            {
                string? tooltipString = translation.TranslateItem(category, itemNameForTooltip, itemName, ProvideDefaultValue);

                if (!string.IsNullOrEmpty(tooltipString))
                {
                    tooltip.SetToolTip(control, tooltipString);
                }
            }
        }

        return true;
    }

    public Func<PropertyInfo, bool>? GetTranslatablePropertyPredicate(object item, Func<PropertyInfo, bool> defaultPredicate)
    {
        if (item is DataGridViewColumn viewCol)
        {
            return property => property.Name.Equals("HeaderText", StringComparison.CurrentCulture) && viewCol.Visible;
        }

        if (item is ComboBox || item is ListBox)
        {
            return property =>
            {
                if (defaultPredicate(property))
                {
                    return true;
                }

                string[] localizableProperties = item.GetType().GetCustomAttribute<LocalizablePropertiesAttribute>()?.TranslatableProperties ?? ["Items"];

                return localizableProperties.Contains(property.Name, StringComparer.Ordinal) &&
                       property.GetValue(item, null) is IList items &&
                       items.Count != 0;
            };
        }

        return null;
    }
}
