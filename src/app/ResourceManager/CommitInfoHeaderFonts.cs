using GitCommands;
using GitExtensions.Extensibility.Extensions;
using ResourceManager.CommitDataRenders;

namespace ResourceManager;

/// <summary>
///  Font selection for the commit-info header - the host-side half of what used to be
///  IHeaderRenderStyleProvider.GetFont (M6): the monospaced layout wants a genuinely
///  fixed-width font, which only the rendering host can determine (it needs a Graphics).
/// </summary>
public static class CommitInfoHeaderFonts
{
    public static Font GetFont(IHeaderRenderStyleProvider styleProvider, Graphics g)
    {
        if (styleProvider is MonospacedHeaderRenderStyleProvider && !AppFonts.App.IsFixedWidth(g))
        {
            return new Font(FontFamily.GenericMonospace, AppFonts.App.Size);
        }

        return AppFonts.App;
    }
}
