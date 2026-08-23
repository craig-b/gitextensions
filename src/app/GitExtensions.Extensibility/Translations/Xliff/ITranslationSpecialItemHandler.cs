using System.Reflection;

namespace GitExtensions.Extensibility.Translations.Xliff;

/// <summary>
///  The host seam that keeps <see cref="TranslationUtil"/> platform-neutral (M7): the generic
///  walk (object fields, string properties matched by name, Item{N} lists) lives in
///  <see cref="TranslationUtil"/>; the WinForms special cases - ToolTip pairing,
///  DataGridViewColumn.HeaderText, ComboBox/ListBox item lists - implement this interface in
///  GitExtensions.Extensibility.WinForms and are installed via
///  <see cref="TranslationUtil.SpecialItemHandler"/> by every WinForms host before any
///  translation walk runs.
/// </summary>
public interface ITranslationSpecialItemHandler
{
    /// <summary>
    ///  Extraction-side hook: if <paramref name="itemObj"/> is a special item, add its
    ///  translation entries and return <see langword="true"/> to skip the generic property walk.
    /// </summary>
    bool TryAddItems(string category, ITranslation translation, string itemName, object itemObj, IEnumerable<(string Name, object Item)> allItems);

    /// <summary>
    ///  Application-side hook, mirroring <see cref="TryAddItems"/>.
    /// </summary>
    bool TryTranslateItems(string category, ITranslation translation, string itemName, object itemObj, IEnumerable<(string Name, object Item)> allItems);

    /// <summary>
    ///  Returns a custom is-this-property-translatable predicate for <paramref name="item"/>,
    ///  or <see langword="null"/> to use <paramref name="defaultPredicate"/> (the name-list
    ///  match against Text/Caption/Title/etc).
    /// </summary>
    Func<PropertyInfo, bool>? GetTranslatablePropertyPredicate(object item, Func<PropertyInfo, bool> defaultPredicate);
}
