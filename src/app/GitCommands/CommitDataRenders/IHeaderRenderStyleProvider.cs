namespace ResourceManager.CommitDataRenders;

/// <summary>
///  Layout parameters for the commit-info header. Platform-neutral (M6): font selection, which
///  needs Graphics, lives host-side in ResourceManager.CommitInfoHeaderFonts.
/// </summary>
public interface IHeaderRenderStyleProvider
{
    int GetMaxWidth();
    IEnumerable<int> GetTabStops();
}
